using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Tests;

/// <summary>Rules the database enforces on its own, checked through the API.</summary>
[Collection(ApiCollection.Name)]
public class SchemaTests(ApiFactory api)
{
    // users_username_lower_uq: "Alice" and "alice" are the same username.
    [Fact]
    public async Task Register_SameUsernameDifferentCase_Returns409()
    {
        var name = "Case" + Guid.NewGuid().ToString("N")[..8];
        using var client = api.CreateClient();
        using var first = await client.PostAsJsonAsync("/api/auth/register",
            new { username = name, email = $"{name}@a.test", password = "12345678" });

        using var second = await client.PostAsJsonAsync("/api/auth/register",
            new { username = name.ToLowerInvariant(), email = $"{name}@b.test", password = "12345678" });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // The trigger owns updated_at; EF reads it back with RETURNING.
    [Fact]
    public async Task UpdateItem_BumpsUpdatedAt()
    {
        var token = await api.RegisterAsync();
        var id = await api.CreateItemAsync(token, price: 5);
        using var client = api.ClientFor(token);
        var before = (await client.GetFromJsonAsync<JsonElement>($"/api/items/{id}")).GetProperty("item");

        using var response = await client.PutAsJsonAsync($"/api/items/{id}", new { price = 6 });

        var after = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("item");
        Assert.True(after.GetProperty("updatedAt").GetDateTimeOffset() > before.GetProperty("updatedAt").GetDateTimeOffset());
    }

    // Purchases RESTRICT deletes: buyers keep what they paid for.
    [Fact]
    public async Task DeleteItem_AfterPurchase_Returns409AndKeepsItem()
    {
        var seller = await api.RegisterAsync();
        var buyer = await api.RegisterAsync();
        var id = await api.CreateItemAsync(seller, price: 5);
        using var buyerClient = api.ClientFor(buyer);
        (await buyerClient.PostAsync($"/api/items/{id}/purchase", null)).EnsureSuccessStatusCode();
        using var sellerClient = api.ClientFor(seller);

        using var response = await sellerClient.DeleteAsync($"/api/items/{id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var itemId = long.Parse(id);
        Assert.True(await api.QueryAsync(db => db.Items.AnyAsync(i => i.Id == itemId)));
    }
}
