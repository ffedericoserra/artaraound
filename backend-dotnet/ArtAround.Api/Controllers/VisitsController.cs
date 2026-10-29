using ArtAround.Api.Data;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Controllers;

[ApiController]
[Route("api/visits")]
public class VisitsController(AppDbContext db, VisitService visits) : ControllerBase
{
    [Authorize]
    [HttpGet("my")]
    public async Task<IActionResult> Mine(long? museumId, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var mine = db.Visits.AsNoTracking().Where(v => v.CreatorId == userId);
        if (museumId is not null)
        {
            mine = mine.Where(v => v.MuseumId == museumId);
        }
        return Ok(new { visits = await visits.Summaries(mine.OrderByDescending(v => v.UpdatedAt)).ToListAsync(ct) });
    }

    /// <summary>Readable by anyone with the link, like in Node; the answer keys only by its author.</summary>
    [HttpGet("{idOrSlug}")]
    public async Task<IActionResult> Get(string idOrSlug, CancellationToken ct)
    {
        var id = await visits.ResolveIdAsync(idOrSlug, ct);
        if (id is null)
        {
            return ApiProblem.Result(StatusCodes.Status404NotFound, "Visit not found");
        }

        // One atomic UPDATE (view_count = view_count + 1), not a read then a write that
        // would lose views under concurrent requests.
        await db.Visits.Where(v => v.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.ViewCount, v => v.ViewCount + 1), ct);

        return Ok(new { visit = await visits.GetDetailAsync(id.Value, User.GetUserId(), ct) });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateVisitRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await visits.CreateAsync(User.RequireUserId(), request, ct));

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, UpdateVisitRequest request, CancellationToken ct) =>
        Ok(await visits.UpdateAsync(User.RequireUserId(), id, request, ct));

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await visits.DeleteAsync(User.RequireUserId(), id, ct);
        return Ok(new { message = "Visit deleted" });
    }
}
