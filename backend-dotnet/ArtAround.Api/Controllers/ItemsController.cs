using ArtAround.Api.Data;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Controllers;

[ApiController]
[Route("api/items")]
public class ItemsController(AppDbContext db, ItemReader reader, ItemService items, PurchaseService purchases)
    : ControllerBase
{
    /// <summary>
    /// Filters: contentId (a universalId), creatorId, targetAudience. Visibility: with
    /// isPublic=true only public items; with any other isPublic value the caller's private
    /// items; without it everything the caller may read (public, own, bought).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        string? contentId, long? creatorId, string? targetAudience, string? isPublic, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var query = reader.WithDetails();

        if (contentId is not null) query = query.Where(i => i.Content.UniversalId == contentId);
        if (creatorId is not null) query = query.Where(i => i.CreatorId == creatorId);
        if (targetAudience is not null) query = query.Where(i => i.TargetAudience == targetAudience);

        query = (isPublic, userId) switch
        {
            ("true", _) => query.Where(i => i.IsPublic),
            (not null, not null) => query.Where(i => !i.IsPublic && i.CreatorId == userId),
            (not null, null) => query.Where(_ => false),
            (null, not null) => query.Where(i =>
                i.IsPublic || i.CreatorId == userId || i.Purchases.Any(p => p.UserId == userId)),
            (null, null) => query.Where(i => i.IsPublic),
        };

        var list = await query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).ToListAsync(ct);
        return Ok(new { items = await reader.ToDtosAsync(list, userId, ct) });
    }

    /// <summary>
    /// A private item is readable by its creator, its buyers, and anyone following a
    /// public visit that uses it.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct)
    {
        var userId = User.GetUserId();
        var item = await reader.WithDetails().SingleOrDefaultAsync(i => i.Id == id, ct);
        if (item is null)
        {
            return ApiProblem.Result(StatusCodes.Status404NotFound, "Item not found");
        }

        var canRead = item.IsPublic
            || item.CreatorId == userId
            || await db.Purchases.AnyAsync(p => p.ItemId == id && p.UserId == userId, ct)
            || await db.VisitStops.AnyAsync(s => s.ItemId == id && db.Visits.Any(v => v.Id == s.VisitId && v.IsPublic), ct);
        if (!canRead)
        {
            return ApiProblem.Result(StatusCodes.Status403Forbidden, "Not authorized to view this item");
        }

        return Ok(new { item = await reader.ToDtoAsync(item, userId, ct) });
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Create(CreateItemRequest request, CancellationToken ct)
    {
        var item = await items.CreateAsync(User.RequireUserId(), request, ct);
        return StatusCode(StatusCodes.Status201Created, new { item });
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, UpdateItemRequest request, CancellationToken ct) =>
        Ok(new { item = await items.UpdateAsync(User.RequireUserId(), id, request, ct) });

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await items.DeleteAsync(User.RequireUserId(), id, ct);
        return Ok(new { message = "Item deleted" });
    }

    [Authorize]
    [HttpPost("{id}/purchase")]
    public async Task<IActionResult> Purchase(long id, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var creatorId = await db.Items.Where(i => i.Id == id).Select(i => (long?)i.CreatorId).SingleOrDefaultAsync(ct);
        if (creatorId is null)
        {
            return ApiProblem.Result(StatusCodes.Status404NotFound, "Item not found");
        }
        if (creatorId == userId)
        {
            return ApiProblem.Result(StatusCodes.Status400BadRequest, "Creators already own their items");
        }

        var result = await purchases.PurchaseAsync(userId, id, ct);
        var message = result.AdoptedItemIds.Count > 0 ? "Item purchased successfully" : "Item already purchased";
        return Ok(new PurchaseResponse(message, result.ChargedAmount, result.WalletBalance, result.AdoptedItemIds));
    }
}
