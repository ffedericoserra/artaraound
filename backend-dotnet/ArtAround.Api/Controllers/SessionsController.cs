using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ArtAround.Api.Controllers;

/// <summary>Group visits (Extension 1). Role checks (owner, participant) live in SessionService.</summary>
[ApiController]
[Authorize]
[Route("api/sessions")]
public class SessionsController(SessionService sessions) : ControllerBase
{
    private long UserId => User.RequireUserId();

    [HttpPost]
    public async Task<IActionResult> Create(CreateSessionRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { session = await sessions.CreateAsync(UserId, request, ct) });

    [HttpGet("my")]
    public async Task<IActionResult> Mine(CancellationToken ct) =>
        Ok(new { sessions = await sessions.GetOwnedAsync(UserId, ct) });

    [HttpGet("{code}")]
    public async Task<IActionResult> Get(string code, CancellationToken ct) =>
        Ok(new { session = await sessions.GetAsync(UserId, code, ct) });

    [HttpPost("{code}/join")]
    public async Task<IActionResult> Join(string code, CancellationToken ct) =>
        Ok(new { session = await sessions.JoinAsync(UserId, code, ct) });

    [HttpPost("{code}/leave")]
    public async Task<IActionResult> Leave(string code, CancellationToken ct)
    {
        await sessions.LeaveAsync(UserId, code, ct);
        return Ok(new { message = "Left session" });
    }

    [HttpPost("{code}/activity")]
    public async Task<IActionResult> LogActivity(string code, LogActivityRequest request, CancellationToken ct)
    {
        await sessions.LogActivityAsync(UserId, code, request.Action!.Value, ct);
        return Ok(new { message = "Activity logged" });
    }

    [HttpPost("{code}/message")]
    public async Task<IActionResult> SendMessage(string code, SendMessageRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new { message = await sessions.SendMessageAsync(UserId, code, request.Text, ct) });

    [HttpPost("{code}/sections/{sectionId}/answers")]
    public async Task<IActionResult> AnswerSection(
        string code, long sectionId, SectionAnswerRequest request, CancellationToken ct)
    {
        var (response, created) = await sessions.AnswerSectionAsync(UserId, code, sectionId, request, ct);
        return StatusCode(created ? StatusCodes.Status201Created : StatusCodes.Status200OK, new { response });
    }

    [HttpGet("{code}/sections/{sectionId}/responses")]
    public async Task<IActionResult> GetSectionResponses(string code, long sectionId, CancellationToken ct) =>
        Ok(new { responses = await sessions.GetSectionResponsesAsync(UserId, code, sectionId, ct) });

    [HttpPost("{code}/quiz/start")]
    public async Task<IActionResult> StartQuiz(string code, CancellationToken ct)
    {
        await sessions.StartQuizAsync(UserId, code, ct);
        return Ok(new { message = "Quiz started" });
    }

    [HttpPost("{code}/quiz")]
    public async Task<IActionResult> SubmitQuiz(string code, SubmitQuizRequest request, CancellationToken ct)
    {
        var (score, total) = await sessions.SubmitQuizAsync(UserId, code, request.Answers, ct);
        return Ok(new { score, total });
    }

    [HttpGet("{code}/quiz")]
    public async Task<IActionResult> GetQuizResults(string code, CancellationToken ct)
    {
        var (results, quiz) = await sessions.GetQuizResultsAsync(UserId, code, ct);
        return Ok(new { results, quiz });
    }

    [HttpPost("{code}/advance")]
    public async Task<IActionResult> Advance(string code, CancellationToken ct) =>
        Ok(await sessions.AdvanceAsync(UserId, code, ct));

    [HttpPost("{code}/previous")]
    public async Task<IActionResult> Previous(string code, CancellationToken ct) =>
        Ok(await sessions.PreviousAsync(UserId, code, ct));

    [HttpPost("{code}/end")]
    public async Task<IActionResult> End(string code, CancellationToken ct)
    {
        await sessions.EndAsync(UserId, code, ct);
        return Ok(new { message = "Session ended" });
    }
}
