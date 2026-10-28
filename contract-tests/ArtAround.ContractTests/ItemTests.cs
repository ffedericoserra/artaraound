using System.Net;
using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

/*
 * GET    /api/items/:id - Read one item (public, or private to creator/buyer)
 * POST   /api/items     - Create item (auth)
 * DELETE /api/items/:id - Delete item (creator only)
 */

/// <summary>An owner, another user and one private item, shared by every test in the class.</summary>
public class PrivateItemFixture : IAsyncLifetime
{
    public const string ItemsApi = "/api/items";

    public string OwnerToken { get; private set; } = "";
    public string OtherToken { get; private set; } = "";
    public string PrivateItemId { get; private set; } = "";
    private string _contentId = "";

    public async Task InitializeAsync()
    {
        OwnerToken = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        OtherToken = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        _contentId = await AnyContentIdAsync();
        PrivateItemId = await CreateItemAsync(OwnerToken, isPublic: false);
    }

    // Unlike users, items can be deleted through the API, so clean up.
    public async Task DisposeAsync() => await DeleteItemAsync(OwnerToken, PrivateItemId);

    public async Task<string> CreateItemAsync(string token, bool isPublic)
    {
        using var response = await Api.SendAsync(HttpMethod.Post, ItemsApi, token, new
        {
            contentId = _contentId,
            targetAudience = "Contract test",
            isPublic,
            descriptions = new[]
            {
                new
                {
                    tone = "easy",
                    texts = new[] { new { text = "Contract test description", lengthCategory = "30s" } },
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await Api.ReadJsonAsync(response);
        return body.GetProperty("item").GetProperty("_id").GetString()!;
    }

    public static async Task DeleteItemAsync(string token, string id)
    {
        using var response = await Api.SendAsync(HttpMethod.Delete, $"{ItemsApi}/{id}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>universalId of the first content of the first seeded museum.</summary>
    private static async Task<string> AnyContentIdAsync()
    {
        using var museums = await Api.SendAsync(HttpMethod.Get, "/api/museums");
        Assert.Equal(HttpStatusCode.OK, museums.StatusCode);
        var museumId = (await Api.ReadJsonAsync(museums))
            .GetProperty("museums")[0].GetProperty("_id").GetString();

        using var contents = await Api.SendAsync(HttpMethod.Get, $"/api/museums/{museumId}/contents");
        Assert.Equal(HttpStatusCode.OK, contents.StatusCode);
        return (await Api.ReadJsonAsync(contents))
            .GetProperty("contents")[0].GetProperty("universalId").GetString()!;
    }
}

public class ItemTests(PrivateItemFixture fixture) : IClassFixture<PrivateItemFixture>
{
    private static string ItemUrl(string id) => $"{PrivateItemFixture.ItemsApi}/{id}";

    /* ---------- ownership ---------- */

    [Fact]
    public async Task GetPrivateItem_AsOwner_Returns200()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, ItemUrl(fixture.PrivateItemId), fixture.OwnerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = (await Api.ReadJsonAsync(response)).GetProperty("item");
        Assert.Equal(fixture.PrivateItemId, item.GetProperty("_id").GetString());
        Assert.True(item.GetProperty("isOwned").GetBoolean());
    }

    [Fact]
    public async Task GetPrivateItem_AsOtherUser_Returns403()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, ItemUrl(fixture.PrivateItemId), fixture.OtherToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await Api.ReadJsonAsync(response)).TryGetProperty("item", out _));
    }

    [Fact]
    public async Task GetPrivateItem_Anonymous_Returns403()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, ItemUrl(fixture.PrivateItemId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False((await Api.ReadJsonAsync(response)).TryGetProperty("item", out _));
    }

    /* ---------- not found ---------- */

    // A deleted item's id is well-formed in whatever id format the backend uses
    // (Mongo ObjectId today, bigint in .NET). A hard-coded id would not be.
    [Fact]
    public async Task GetItem_Deleted_Returns404()
    {
        var id = await fixture.CreateItemAsync(fixture.OwnerToken, isPublic: true);
        await PrivateItemFixture.DeleteItemAsync(fixture.OwnerToken, id);

        using var response = await Api.SendAsync(HttpMethod.Get, ItemUrl(id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Item not found", (await Api.ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    // Malformed in every id format. A client error, so 400, never 500.
    [Fact]
    public async Task GetItem_MalformedId_Returns400()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, ItemUrl("not-an-id"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("ObjectId", (await Api.ReadJsonAsync(response)).GetProperty("error").GetString());
    }
}
