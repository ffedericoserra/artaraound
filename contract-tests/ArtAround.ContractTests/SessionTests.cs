using System.Net;
using System.Text.Json;
using static ArtAround.ContractTests.TestData;

namespace ArtAround.ContractTests;

/*
 * POST /api/sessions               - Host a group session of a synchronized visit (auth)
 * GET  /api/sessions/:code         - Read an active session
 * POST /api/sessions/:code/join    - Join as participant
 * POST /api/sessions/:code/advance - Next step (owner only)
 * POST /api/sessions/:code/message - Chat (owner or participant)
 * POST /api/sessions/:code/end     - End (owner only)
 *
 * Each test hosts its own session: navigation and ending change it.
 */

/// <summary>A teacher, a student and a public synchronized visit with three stops.</summary>
public class GroupVisitFixture : IAsyncLifetime
{
    public string TeacherToken { get; private set; } = "";
    public string StudentToken { get; private set; } = "";
    public string StudentName { get; } = UniqueUsername(12);
    public string SyncVisitId { get; private set; } = "";
    public string StandardVisitId { get; private set; } = "";
    private string _itemId = "";

    public async Task InitializeAsync()
    {
        TeacherToken = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));
        StudentToken = await Api.RegisterAndGetTokenAsync(StudentName);
        var (museumId, artworks) = await Resources.SeededMuseumAsync();
        _itemId = await Resources.CreateItemAsync(TeacherToken, artworks[0], isPublic: true, price: 0);

        var sync = await Resources.CreateVisitAsync(TeacherToken, museumId, [_itemId, _itemId, _itemId], type: "synchronized");
        SyncVisitId = sync.GetProperty("visit").GetProperty("_id").GetString()!;
        var standard = await Resources.CreateVisitAsync(TeacherToken, museumId, [_itemId]);
        StandardVisitId = standard.GetProperty("visit").GetProperty("_id").GetString()!;
    }

    public async Task DisposeAsync()
    {
        await Resources.DeleteVisitAsync(TeacherToken, SyncVisitId);
        await Resources.DeleteVisitAsync(TeacherToken, StandardVisitId);
        await PrivateItemFixture.DeleteItemAsync(TeacherToken, _itemId);
    }

    /// <summary>Hosts a session with a unique custom code and returns the code.</summary>
    public async Task<string> HostAsync()
    {
        var code = "CT_" + RandomString(10).ToUpperInvariant();
        using var response = await Api.SendAsync(HttpMethod.Post, Resources.SessionsApi, TeacherToken,
            new { visitId = SyncVisitId, code });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await Api.ReadJsonAsync(response)).GetProperty("session").GetProperty("code").GetString()!;
    }
}

public class SessionTests(GroupVisitFixture fixture) : IClassFixture<GroupVisitFixture>
{
    private static string Url(string code, string action = "") =>
        $"{Resources.SessionsApi}/{Uri.EscapeDataString(code)}{(action == "" ? "" : "/" + action)}";

    private static async Task<JsonElement> PostOkAsync(string url, string token, object? body = null)
    {
        using var response = await Api.SendAsync(HttpMethod.Post, url, token, body);
        Assert.True(response.IsSuccessStatusCode, $"POST {url}: {response.StatusCode}");
        return await Api.ReadJsonAsync(response);
    }

    [Fact]
    public async Task Create_SynchronizedVisit_HostIsOwner()
    {
        var code = await fixture.HostAsync();

        using var response = await Api.SendAsync(HttpMethod.Get, Url(code), fixture.TeacherToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var session = (await Api.ReadJsonAsync(response)).GetProperty("session");
        Assert.True(session.GetProperty("isOwner").GetBoolean());
        Assert.Equal(0, session.GetProperty("currentStepIndex").GetInt32());
    }

    [Fact]
    public async Task Create_StandardVisit_Returns400()
    {
        using var response = await Api.SendAsync(HttpMethod.Post, Resources.SessionsApi, fixture.TeacherToken,
            new { visitId = fixture.StandardVisitId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_CodeInUse_Returns409()
    {
        var code = await fixture.HostAsync();

        using var response = await Api.SendAsync(HttpMethod.Post, Resources.SessionsApi, fixture.TeacherToken,
            new { visitId = fixture.SyncVisitId, code });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Join_AsStudent_AddsParticipantWhoIsNotOwner()
    {
        var code = await fixture.HostAsync();

        var session = (await PostOkAsync(Url(code, "join"), fixture.StudentToken)).GetProperty("session");

        Assert.False(session.GetProperty("isOwner").GetBoolean());
        Assert.Contains(session.GetProperty("participants").EnumerateArray(),
            p => p.GetProperty("username").GetString() == fixture.StudentName);
    }

    // Codes are case-insensitive: students type them by hand.
    [Fact]
    public async Task Join_LowerCaseCode_FindsSession()
    {
        var code = await fixture.HostAsync();

        using var response = await Api.SendAsync(HttpMethod.Post, Url(code.ToLowerInvariant(), "join"), fixture.StudentToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Advance_AsOwner_MovesOneStep()
    {
        var code = await fixture.HostAsync();

        var body = await PostOkAsync(Url(code, "advance"), fixture.TeacherToken);

        Assert.Equal(1, body.GetProperty("currentStepIndex").GetInt32());
        Assert.Equal(1, body.GetProperty("currentItemIndex").GetInt32());
        Assert.False(body.GetProperty("isLast").GetBoolean());
    }

    [Fact]
    public async Task Advance_PastLastStep_StaysOnLast()
    {
        var code = await fixture.HostAsync();
        for (var i = 0; i < 3; i++)
        {
            await PostOkAsync(Url(code, "advance"), fixture.TeacherToken);
        }

        var body = await PostOkAsync(Url(code, "advance"), fixture.TeacherToken);

        Assert.Equal(2, body.GetProperty("currentStepIndex").GetInt32());
        Assert.True(body.GetProperty("isLast").GetBoolean());
    }

    [Fact]
    public async Task Advance_AsParticipant_Returns403()
    {
        var code = await fixture.HostAsync();
        await PostOkAsync(Url(code, "join"), fixture.StudentToken);

        using var response = await Api.SendAsync(HttpMethod.Post, Url(code, "advance"), fixture.StudentToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_AsParticipant_Returns201WithMessage()
    {
        var code = await fixture.HostAsync();
        await PostOkAsync(Url(code, "join"), fixture.StudentToken);

        using var response = await Api.SendAsync(HttpMethod.Post, Url(code, "message"), fixture.StudentToken,
            new { text = "  ciao  " });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var message = (await Api.ReadJsonAsync(response)).GetProperty("message");
        Assert.Equal("ciao", message.GetProperty("text").GetString());
        Assert.Equal(fixture.StudentName, message.GetProperty("username").GetString());
    }

    [Fact]
    public async Task SendMessage_AsOutsider_Returns403()
    {
        var code = await fixture.HostAsync();
        var outsider = await Api.RegisterAndGetTokenAsync(UniqueUsername(12));

        using var response = await Api.SendAsync(HttpMethod.Post, Url(code, "message"), outsider, new { text = "hi" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task End_AsOwner_SessionNoLongerFound()
    {
        var code = await fixture.HostAsync();

        await PostOkAsync(Url(code, "end"), fixture.TeacherToken);

        using var response = await Api.SendAsync(HttpMethod.Get, Url(code), fixture.TeacherToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
