using System.Net;
using System.Text.Json;
using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

/*
 * POST   /api/visits     - Create a visit; buys the stops' items the author does not own (auth)
 * GET    /api/visits/:id - Read a visit by id or slug; the quiz key only for its author
 * PUT    /api/visits/:id - Update (creator only)
 * DELETE /api/visits/:id - Delete (creator only)
 * POST   /api/auth/favorites/visits/:id - Toggle a saved visit (auth)
 */

/// <summary>An author, another user, and a public visit with a two-question quiz over the author's own item.</summary>
public class AuthoredVisitFixture : IAsyncLifetime
{
    public string AuthorToken { get; private set; } = "";
    public string OtherToken { get; private set; } = "";
    public string MuseumId { get; private set; } = "";
    public string ItemId { get; private set; } = "";
    public JsonElement Visit { get; private set; }
    public string VisitId => Visit.GetProperty("_id").GetString()!;

    public async Task InitializeAsync()
    {
        AuthorToken = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        OtherToken = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        (MuseumId, var artworks) = await Resources.SeededMuseumAsync();
        ItemId = await Resources.CreateItemAsync(AuthorToken, artworks[0], isPublic: true, price: 0);
        var quiz = new[]
        {
            new { question = "Q1", options = new[] { "a", "b" }, correctIndex = 1 },
            new { question = "Q2", options = new[] { "a", "b", "c" }, correctIndex = 2 },
        };
        Visit = (await Resources.CreateVisitAsync(AuthorToken, MuseumId, [ItemId], quiz: quiz)).GetProperty("visit");
    }

    public async Task DisposeAsync()
    {
        await Resources.DeleteVisitAsync(AuthorToken, VisitId);
        await PrivateItemFixture.DeleteItemAsync(AuthorToken, ItemId);
    }
}

public class VisitTests(AuthoredVisitFixture fixture) : IClassFixture<AuthoredVisitFixture>
{
    private static string VisitUrl(string id) => $"{Resources.VisitsApi}/{id}";

    private static async Task<JsonElement> GetVisitAsync(string idOrSlug, string? token = null)
    {
        using var response = await Api.SendAsync(HttpMethod.Get, VisitUrl(idOrSlug), token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Api.ReadJsonAsync(response)).GetProperty("visit");
    }

    /* ---------- read ---------- */

    [Fact]
    public async Task GetVisit_ById_ReturnsStopsWithFullItems()
    {
        var visit = await GetVisitAsync(fixture.VisitId);

        Assert.Equal(fixture.VisitId, visit.GetProperty("_id").GetString());
        var stop = visit.GetProperty("sequence")[0];
        Assert.Equal(fixture.ItemId, stop.GetProperty("itemId").GetProperty("_id").GetString());
        Assert.Equal(fixture.MuseumId, visit.GetProperty("museumId").GetProperty("_id").GetString());
    }

    [Fact]
    public async Task GetVisit_BySlug_ReturnsSameVisit()
    {
        var visit = await GetVisitAsync(fixture.Visit.GetProperty("slug").GetString()!);

        Assert.Equal(fixture.VisitId, visit.GetProperty("_id").GetString());
    }

    // The navigator reads the quiz from this endpoint: participants must not see the answers.
    [Fact]
    public async Task GetVisit_AsOtherUser_HidesQuizAnswerKey()
    {
        var quiz = (await GetVisitAsync(fixture.VisitId, fixture.OtherToken)).GetProperty("quiz");

        Assert.Equal(2, quiz.GetArrayLength());
        Assert.All(quiz.EnumerateArray(), q =>
        {
            Assert.True(q.TryGetProperty("options", out _));
            Assert.False(q.TryGetProperty("correctIndex", out _));
        });
    }

    [Fact]
    public async Task GetVisit_AsAuthor_IncludesQuizAnswerKey()
    {
        var quiz = (await GetVisitAsync(fixture.VisitId, fixture.AuthorToken)).GetProperty("quiz");

        Assert.Equal(1, quiz[0].GetProperty("correctIndex").GetInt32());
    }

    [Fact]
    public async Task GetVisit_Deleted_Returns404()
    {
        var created = await Resources.CreateVisitAsync(fixture.AuthorToken, fixture.MuseumId, [fixture.ItemId]);
        var id = created.GetProperty("visit").GetProperty("_id").GetString()!;
        await Resources.DeleteVisitAsync(fixture.AuthorToken, id);

        using var response = await Api.SendAsync(HttpMethod.Get, VisitUrl(id));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Visit not found", (await Api.ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task ListMuseumVisits_ContainsPublicVisit()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, $"/api/museums/{fixture.MuseumId}/visits");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var visits = (await Api.ReadJsonAsync(response)).GetProperty("visits");
        Assert.Contains(visits.EnumerateArray(), v => v.GetProperty("_id").GetString() == fixture.VisitId);
    }

    /* ---------- write ---------- */

    [Fact]
    public async Task CreateVisit_WithOwnFreeItem_ChargesNothing()
    {
        var body = await Resources.CreateVisitAsync(fixture.AuthorToken, fixture.MuseumId, [fixture.ItemId]);

        Assert.Equal(0, body.GetProperty("chargedAmount").GetInt32());
        await Resources.DeleteVisitAsync(fixture.AuthorToken, body.GetProperty("visit").GetProperty("_id").GetString()!);
    }

    [Fact]
    public async Task CreateVisit_WithoutTitle_Returns400()
    {
        using var response = await Api.SendAsync(HttpMethod.Post, Resources.VisitsApi, fixture.AuthorToken,
            new { museumId = fixture.MuseumId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateVisit_AsOtherUser_Returns403()
    {
        using var response = await Api.SendAsync(HttpMethod.Put, VisitUrl(fixture.VisitId), fixture.OtherToken,
            new { title = "Hijacked" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual("Hijacked", (await GetVisitAsync(fixture.VisitId)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task DeleteVisit_AsOtherUser_Returns403()
    {
        using var response = await Api.SendAsync(HttpMethod.Delete, VisitUrl(fixture.VisitId), fixture.OtherToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // Items used by a visit cannot be deleted: the visit would lose a stop.
    [Fact]
    public async Task DeleteItem_UsedByVisit_Returns409()
    {
        using var response = await Api.SendAsync(HttpMethod.Delete, $"{Resources.ItemsApi}/{fixture.ItemId}", fixture.AuthorToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /* ---------- favourites ---------- */

    [Fact]
    public async Task ToggleFavorite_Twice_SavesThenRemoves()
    {
        var token = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        var url = $"/api/auth/favorites/visits/{fixture.VisitId}";

        using var first = await Api.SendAsync(HttpMethod.Post, url, token);
        using var second = await Api.SendAsync(HttpMethod.Post, url, token);

        Assert.True((await Api.ReadJsonAsync(first)).GetProperty("isSaved").GetBoolean());
        Assert.False((await Api.ReadJsonAsync(second)).GetProperty("isSaved").GetBoolean());
    }
}
