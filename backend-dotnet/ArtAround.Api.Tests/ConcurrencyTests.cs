using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Tests;

/// <summary>
/// Each test fires requests at the same time and checks an invariant that a
/// read-check-write without locks would break: no negative balance, no double charge,
/// no lost update. Starting the tasks before awaiting any is what makes them concurrent.
/// </summary>
[Collection(ApiCollection.Name)]
public class ConcurrencyTests(ApiFactory api)
{
    [Fact]
    public async Task ParallelPurchases_BalanceCoversOne_ExactlyOneSucceeds()
    {
        // Arrange: 100 credits, two items at 60 each.
        var seller = await api.RegisterAsync();
        var buyer = await api.RegisterAsync();
        var first = await api.CreateItemAsync(seller, price: 60);
        var second = await api.CreateItemAsync(seller, price: 60);
        using var client = api.ClientFor(buyer);

        // Act
        var responses = await Task.WhenAll(
            client.PostAsync($"/api/items/{first}/purchase", null),
            client.PostAsync($"/api/items/{second}/purchase", null));

        // Assert
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.BadRequest);
        Assert.Equal(40, await api.BalanceAsync(buyer));
        Assert.Equal(160, await api.BalanceAsync(seller));
    }

    [Fact]
    public async Task SamePurchaseTenTimesInParallel_ChargedOnce()
    {
        var seller = await api.RegisterAsync();
        var buyer = await api.RegisterAsync();
        var item = await api.CreateItemAsync(seller, price: 10);
        using var client = api.ClientFor(buyer);

        var responses = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => client.PostAsync($"/api/items/{item}/purchase", null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(90, await api.BalanceAsync(buyer));
        var itemId = long.Parse(item);
        Assert.Equal(1, await api.QueryAsync(db => db.Purchases.CountAsync(p => p.ItemId == itemId)));
    }

    // Two buyers buying each other's items lock the same two user rows. Locking in id
    // order means they queue instead of deadlocking (Postgres would abort one with 40P01).
    [Fact]
    public async Task CrossPurchasesInParallel_NoDeadlock()
    {
        var alice = await api.RegisterAsync();
        var bob = await api.RegisterAsync();
        var aliceItems = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.CreateItemAsync(alice, price: 1)));
        var bobItems = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => api.CreateItemAsync(bob, price: 1)));
        using var aliceClient = api.ClientFor(alice);
        using var bobClient = api.ClientFor(bob);

        var responses = await Task.WhenAll(
            bobItems.Select(i => aliceClient.PostAsync($"/api/items/{i}/purchase", null))
                .Concat(aliceItems.Select(i => bobClient.PostAsync($"/api/items/{i}/purchase", null))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(100, await api.BalanceAsync(alice));
        Assert.Equal(100, await api.BalanceAsync(bob));
    }

    [Fact]
    public async Task ParallelRecharges_NoLostUpdate()
    {
        var user = await api.RegisterAsync();
        using var client = api.ClientFor(user);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.PatchAsJsonAsync("/api/auth/wallet", new { amount = 5 })));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(200, await api.BalanceAsync(user));
    }

    // The charge and the visit commit together: a visit the wallet cannot pay for is not saved.
    [Fact]
    public async Task CreateVisit_InsufficientBalance_SavesNothing()
    {
        var seller = await api.RegisterAsync();
        var buyer = await api.RegisterAsync();
        var items = new[] { await api.CreateItemAsync(seller, 60), await api.CreateItemAsync(seller, 60) };
        var museumId = await api.QueryAsync(db => db.Museums.Select(m => m.Id).FirstAsync());
        using var client = api.ClientFor(buyer);

        using var response = await client.PostAsJsonAsync("/api/visits", new
        {
            title = "Too expensive",
            museumId,
            sequence = items.Select(itemId => new { itemId }),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var mine = await client.GetFromJsonAsync<JsonElement>("/api/visits/my");
        Assert.Equal(0, mine.GetProperty("visits").GetArrayLength());
        Assert.Equal(100, await api.BalanceAsync(buyer));
    }

    // xmin guards current_step_index: every click either moves the session one step or
    // gets a 409. Without it two clicks could read step 0 and both write step 1.
    [Fact]
    public async Task ParallelAdvances_EachSuccessMovesOneStep()
    {
        var teacher = await api.RegisterAsync();
        var item = await api.CreateItemAsync(teacher, price: 0);
        var museumId = await api.QueryAsync(db => db.Museums.Select(m => m.Id).FirstAsync());
        using var client = api.ClientFor(teacher);
        using var visit = await client.PostAsJsonAsync("/api/visits", new
        {
            title = "Sync",
            museumId,
            type = "synchronized",
            sequence = Enumerable.Repeat(new { itemId = item }, 10),
        });
        var visitId = (await visit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("visit").GetProperty("_id").GetString();
        using var created = await client.PostAsJsonAsync("/api/sessions", new { visitId });
        var code = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("session").GetProperty("code").GetString();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => client.PostAsync($"/api/sessions/{code}/advance", null)));

        Assert.All(responses, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        var session = await client.GetFromJsonAsync<JsonElement>($"/api/sessions/{code}");
        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(successes, session.GetProperty("session").GetProperty("currentStepIndex").GetInt32());
    }
}
