using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Controllers;

[ApiController]
[Route("api/museums")]
public class MuseumsController(AppDbContext db, VisitService visits) : ControllerBase
{
    private const string NotFoundMessage = "Museum not found";

    private static readonly Dictionary<string, ContentType> ContentTypes = new()
    {
        ["Artwork"] = ContentType.Artwork,
        ["Artist"] = ContentType.Artist,
        ["Movement"] = ContentType.Movement,
        ["Place"] = ContentType.Place,
    };

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        // COLLATE "C" sorts by byte value, like Mongo; the column's default collation is
        // locale-aware and would put "a" before "B".
        var museums = await db.Museums
            .AsNoTracking()
            .OrderBy(m => EF.Functions.Collate(m.Name, "C"))
            .Select(m => new MuseumSummaryDto(m.Id, m.Name, m.Slug, m.Address, m.Description, m.ImageUrl))
            .ToListAsync(ct);
        return Ok(new { museums });
    }

    [HttpGet("{idOrSlug}")]
    public async Task<IActionResult> Get(string idOrSlug, CancellationToken ct)
    {
        var museum = await ByIdOrSlug(idOrSlug)
            .AsNoTracking()
            .Include(m => m.FloorPlans)
            .Include(m => m.PointsOfInterest)
            .AsSplitQuery()
            .SingleOrDefaultAsync(ct);
        return museum is null
            ? ApiProblem.Result(StatusCodes.Status404NotFound, NotFoundMessage)
            : Ok(new { museum = MuseumDto.From(museum) });
    }

    /// <summary>?type= must be an exact ContentType name: "artist" or an empty value is a 400.</summary>
    [HttpGet("{idOrSlug}/contents")]
    public async Task<IActionResult> GetContents(string idOrSlug, CancellationToken ct)
    {
        ContentType? type = null;
        // Read the raw query: model binding would turn "?type=" into null, and enum
        // binding is case-insensitive.
        if (Request.Query.TryGetValue("type", out var raw))
        {
            if (!ContentTypes.TryGetValue(raw.ToString(), out var parsed))
            {
                return ApiProblem.ValidationResult("type", "type must be one of: Artwork, Artist, Movement, Place");
            }
            type = parsed;
        }

        var museumId = await ByIdOrSlug(idOrSlug).Select(m => (long?)m.Id).SingleOrDefaultAsync(ct);
        if (museumId is null)
        {
            return ApiProblem.Result(StatusCodes.Status404NotFound, NotFoundMessage);
        }

        var query = db.Contents.AsNoTracking().Where(c => c.MuseumId == museumId);
        if (type is not null)
        {
            query = query.Where(c => c.Type == type);
        }
        var contents = await query.OrderBy(c => EF.Functions.Collate(c.Name, "C")).ToListAsync(ct);
        return Ok(new { contents = contents.Select(ContentDto.From) });
    }

    /// <summary>Public visits of the museum, plus the caller's own private ones.</summary>
    [HttpGet("{idOrSlug}/visits")]
    public async Task<IActionResult> GetVisits(string idOrSlug, CancellationToken ct)
    {
        var museumId = await ByIdOrSlug(idOrSlug).Select(m => (long?)m.Id).SingleOrDefaultAsync(ct);
        if (museumId is null)
        {
            return ApiProblem.Result(StatusCodes.Status404NotFound, NotFoundMessage);
        }

        var userId = User.GetUserId();
        var visible = db.Visits.AsNoTracking()
            .Where(v => v.MuseumId == museumId && (v.IsPublic || v.CreatorId == userId))
            .OrderByDescending(v => v.ViewCount).ThenBy(v => v.Id);
        return Ok(new { visits = await visits.Summaries(visible).ToListAsync(ct) });
    }

    /// <summary>Any string is a valid slug, so an identifier that is not a number is looked up as one.</summary>
    private IQueryable<Museum> ByIdOrSlug(string idOrSlug) =>
        long.TryParse(idOrSlug, out var id)
            ? db.Museums.Where(m => m.Id == id)
            : db.Museums.Where(m => m.Slug == idOrSlug);
}
