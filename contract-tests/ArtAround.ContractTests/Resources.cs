using System.Net;
using System.Text.Json;

namespace ArtAround.ContractTests;

/// <summary>Creates the items, visits and sessions the tests need, through the public API.</summary>
internal static class Resources
{
    public const string ItemsApi = "/api/items";
    public const string VisitsApi = "/api/visits";
    public const string SessionsApi = "/api/sessions";

    /// <summary>The seeded Uffizi: its id and the universalIds of a few of its artworks.</summary>
    public static async Task<(string MuseumId, List<string> ArtworkIds)> SeededMuseumAsync()
    {
        using var museum = await Api.SendAsync(HttpMethod.Get, $"/api/museums/{SeededMuseumFixture.Slug}");
        Assert.Equal(HttpStatusCode.OK, museum.StatusCode);
        var museumId = (await Api.ReadJsonAsync(museum)).GetProperty("museum").GetProperty("_id").GetString()!;

        using var contents = await Api.SendAsync(HttpMethod.Get, $"/api/museums/{museumId}/contents?type=Artwork");
        Assert.Equal(HttpStatusCode.OK, contents.StatusCode);
        var ids = (await Api.ReadJsonAsync(contents)).GetProperty("contents").EnumerateArray()
            .Select(c => c.GetProperty("universalId").GetString()!).ToList();
        return (museumId, ids);
    }

    /// <summary>Same dual-shape payload as PrivateItemFixture: accepted by Node and by .NET.</summary>
    public static async Task<string> CreateItemAsync(string token, string contentId, bool isPublic, int price)
    {
        using var response = await Api.SendAsync(HttpMethod.Post, ItemsApi, token, new
        {
            contentId,
            targetAudience = "general",
            isPublic,
            price,
            descriptions = new[]
            {
                new
                {
                    tone = "easy",
                    text = "Contract test description",
                    texts = new[] { new { text = "Contract test description", lengthCategory = "30s" } },
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Api.ReadJsonAsync(response)).GetProperty("item").GetProperty("_id").GetString()!;
    }

    /// <summary>Hides a test item from the marketplace. Bought items cannot be deleted in .NET.</summary>
    public static async Task HideItemAsync(string token, string itemId)
    {
        using var response = await Api.SendAsync(HttpMethod.Put, $"{ItemsApi}/{itemId}", token, new { isPublic = false });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<JsonElement> CreateVisitAsync(
        string token, string museumId, IEnumerable<string> itemIds, string type = "standard", object? quiz = null)
    {
        using var response = await Api.SendAsync(HttpMethod.Post, VisitsApi, token, new
        {
            title = "Contract test visit " + TestData.RandomString(6),
            museumId,
            type,
            isPublic = true,
            sequence = itemIds.Select(itemId => new { itemId }).ToArray(),
            quiz = quiz ?? Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await Api.ReadJsonAsync(response);
    }

    public static async Task DeleteVisitAsync(string token, string visitId)
    {
        using var response = await Api.SendAsync(HttpMethod.Delete, $"{VisitsApi}/{visitId}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public static async Task<int> WalletBalanceAsync(string token)
    {
        using var response = await Api.SendAsync(HttpMethod.Get, "/api/auth/me", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Api.ReadJsonAsync(response)).GetProperty("user").GetProperty("walletBalance").GetInt32();
    }
}
