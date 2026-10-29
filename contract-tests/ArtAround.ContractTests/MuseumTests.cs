using System.Net;
using System.Text.Json;

namespace ArtAround.ContractTests;

/*
 * GET /api/museums                         - List museums (summary fields), sorted by name
 * GET /api/museums/:idOrSlug               - One museum, full detail
 * GET /api/museums/:idOrSlug/contents?type - Its contents, optionally filtered by type (400 if invalid), sorted by name
 *
 * Data comes from the seed (data/museums/*.json). These endpoints are read-only and there is
 * no DELETE for museums, so creating one per run would leave garbage behind. Tests only read,
 * so sharing the seed keeps them independent. They assert properties, never exact counts.
 */

/// <summary>The seeded Uffizi museum, read once and shared by every test in the class.</summary>
public class SeededMuseumFixture : IAsyncLifetime
{
    public const string MuseumsApi = "/api/museums";
    public const string Slug = "galleria-degli-uffizi";

    public string Id { get; private set; } = "";

    public async Task InitializeAsync()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, $"{MuseumsApi}/{Slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Id = (await Api.ReadJsonAsync(response)).GetProperty("museum").GetProperty("_id").GetString()!;
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public class MuseumTests(SeededMuseumFixture fixture) : IClassFixture<SeededMuseumFixture>
{
    private const string MuseumsApi = SeededMuseumFixture.MuseumsApi;

    private static async Task<JsonElement> GetOkAsync(string url, string property)
    {
        using var response = await Api.SendAsync(HttpMethod.Get, url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await Api.ReadJsonAsync(response)).GetProperty(property);
    }

    private static List<string> Names(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToList();

    /* ---------- list ---------- */

    [Fact]
    public async Task ListMuseums_ContainsSeededMuseumWithSummaryFields()
    {
        var museums = await GetOkAsync(MuseumsApi, "museums");

        var uffizi = museums.EnumerateArray().Single(m => m.GetProperty("slug").GetString() == SeededMuseumFixture.Slug);
        Assert.Equal(fixture.Id, uffizi.GetProperty("_id").GetString());
        Assert.Equal("Galleria degli Uffizi", uffizi.GetProperty("name").GetString());
        Assert.True(uffizi.TryGetProperty("address", out _));
        Assert.True(uffizi.TryGetProperty("imageUrl", out _));
        // The list is a summary: detail-only fields stay out of it.
        Assert.False(uffizi.TryGetProperty("floorPlans", out _));
    }

    // Ordinal, like Mongo's sort. Postgres sorts by the column's collation, which may differ.
    [Fact]
    public async Task ListMuseums_SortedByName()
    {
        var names = Names(await GetOkAsync(MuseumsApi, "museums"));

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
    }

    /* ---------- detail ---------- */

    [Fact]
    public async Task GetMuseum_BySlug_ReturnsFullDetail()
    {
        var museum = await GetOkAsync($"{MuseumsApi}/{SeededMuseumFixture.Slug}", "museum");

        Assert.Equal(fixture.Id, museum.GetProperty("_id").GetString());
        Assert.Equal(SeededMuseumFixture.Slug, museum.GetProperty("slug").GetString());

        var floorPlan = museum.GetProperty("floorPlans")[0];
        Assert.Equal(JsonValueKind.Number, floorPlan.GetProperty("floor").ValueKind);
        Assert.True(floorPlan.TryGetProperty("label", out _));
        Assert.True(floorPlan.TryGetProperty("imageUrl", out _));

        var poi = museum.GetProperty("pointsOfInterest")[0];
        Assert.True(poi.TryGetProperty("type", out _));
        Assert.True(poi.TryGetProperty("label", out _));
        Assert.Equal(JsonValueKind.Number, poi.GetProperty("coordinates").GetProperty("lat").ValueKind);
    }

    [Fact]
    public async Task GetMuseum_ById_ReturnsSameMuseumAsBySlug()
    {
        var museum = await GetOkAsync($"{MuseumsApi}/{fixture.Id}", "museum");

        Assert.Equal(fixture.Id, museum.GetProperty("_id").GetString());
        Assert.Equal(SeededMuseumFixture.Slug, museum.GetProperty("slug").GetString());
    }

    // Any string is a valid slug, so an unknown or malformed identifier is 404, not 400 as for items.
    [Theory]
    [InlineData("no-such-museum")]
    [InlineData("not an id!")]
    public async Task GetMuseum_UnknownIdentifier_Returns404(string identifier)
    {
        using var response = await Api.SendAsync(HttpMethod.Get, $"{MuseumsApi}/{Uri.EscapeDataString(identifier)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Museum not found", (await Api.ReadJsonAsync(response)).GetProperty("error").GetString());
    }

    /* ---------- contents ---------- */

    [Fact]
    public async Task GetContents_NoFilter_ReturnsOnlyThisMuseumsContents()
    {
        var contents = await GetOkAsync($"{MuseumsApi}/{SeededMuseumFixture.Slug}/contents", "contents");

        Assert.NotEmpty(contents.EnumerateArray());
        Assert.All(contents.EnumerateArray(), c =>
        {
            Assert.Equal(fixture.Id, c.GetProperty("museumId").GetString());
            Assert.False(string.IsNullOrEmpty(c.GetProperty("_id").GetString()));
            Assert.False(string.IsNullOrEmpty(c.GetProperty("universalId").GetString()));
        });
    }

    [Fact]
    public async Task GetContents_ById_SameAsBySlug()
    {
        var bySlug = await GetOkAsync($"{MuseumsApi}/{SeededMuseumFixture.Slug}/contents", "contents");
        var byId = await GetOkAsync($"{MuseumsApi}/{fixture.Id}/contents", "contents");

        Assert.Equal(Names(bySlug), Names(byId));
    }

    [Fact]
    public async Task GetContents_SortedByName()
    {
        var names = Names(await GetOkAsync($"{MuseumsApi}/{SeededMuseumFixture.Slug}/contents", "contents"));

        Assert.Equal(names.Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public async Task GetContents_FilterByType_ReturnsOnlyThatType()
    {
        var url = $"{MuseumsApi}/{SeededMuseumFixture.Slug}/contents";
        var all = await GetOkAsync(url, "contents");
        var artists = await GetOkAsync($"{url}?type=Artist", "contents");

        Assert.NotEmpty(artists.EnumerateArray());
        Assert.All(artists.EnumerateArray(), c => Assert.Equal("Artist", c.GetProperty("type").GetString()));
        Assert.True(artists.GetArrayLength() < all.GetArrayLength());
    }

    // type is an enum matched exactly: a wrong case, an unknown value or an empty value is a client error.
    // Only the status is asserted: the 400 body shape changes to ProblemDetails in Stage 6.
    [Theory]
    [InlineData("artist")]
    [InlineData("Bogus")]
    [InlineData("")]
    public async Task GetContents_InvalidType_Returns400(string type)
    {
        using var response = await Api.SendAsync(HttpMethod.Get, $"{MuseumsApi}/{SeededMuseumFixture.Slug}/contents?type={type}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetContents_UnknownMuseum_Returns404()
    {
        using var response = await Api.SendAsync(HttpMethod.Get, $"{MuseumsApi}/no-such-museum/contents");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Museum not found", (await Api.ReadJsonAsync(response)).GetProperty("error").GetString());
    }
}
