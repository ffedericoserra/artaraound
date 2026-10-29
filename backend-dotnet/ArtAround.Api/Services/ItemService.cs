using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Services;

public class ItemService(AppDbContext db, ItemReader reader)
{
    public async Task<ItemDto> CreateAsync(long userId, CreateItemRequest request, CancellationToken ct)
    {
        var content = await db.Contents.SingleOrDefaultAsync(c => c.UniversalId == request.ContentId, ct)
            ?? throw ApiException.BadRequest("contentId must match an existing Content.universalId");

        var item = new Item
        {
            ContentId = content.Id,
            CreatorId = userId,
            TargetAudience = request.TargetAudience,
            Price = request.Price,
            License = request.License,
            IsPublic = request.IsPublic,
            Texts = ToTexts(request.Descriptions),
            AssociatedContents = await AssociatedContentsAsync(content, request.AssociatedContents, ct),
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(item.Id, userId, ct);
    }

    /// <summary>
    /// Read-modify-write, guarded by the xmin concurrency token: if another request
    /// changed the item since we read it, SaveChanges matches no row and throws
    /// DbUpdateConcurrencyException (409) instead of silently overwriting it.
    /// </summary>
    public async Task<ItemDto> UpdateAsync(long userId, long id, UpdateItemRequest request, CancellationToken ct)
    {
        var item = await db.Items
            .Include(i => i.Content)
            .Include(i => i.Texts)
            .Include(i => i.AssociatedContents)
            .SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw ApiException.NotFound("Item not found");
        if (item.CreatorId != userId)
        {
            throw ApiException.Forbidden("Not authorized to update this item");
        }

        if (request.TargetAudience is not null) item.TargetAudience = request.TargetAudience;
        if (request.Price is not null) item.Price = request.Price.Value;
        if (request.License is not null) item.License = request.License.Value;
        if (request.IsPublic is not null) item.IsPublic = request.IsPublic.Value;
        if (request.Descriptions is not null)
        {
            // Texts are keyed by (item_id, tone): update the tones that stay, so EF never
            // deletes and re-inserts the same key in one SaveChanges.
            var wanted = ToTexts(request.Descriptions).ToDictionary(t => t.Tone);
            item.Texts.RemoveAll(t => !wanted.ContainsKey(t.Tone));
            foreach (var (tone, text) in wanted)
            {
                var existing = item.Texts.SingleOrDefault(t => t.Tone == tone);
                if (existing is null) item.Texts.Add(text);
                else existing.Text = text.Text;
            }
        }
        if (request.AssociatedContents is not null)
        {
            var contents = await AssociatedContentsAsync(item.Content, request.AssociatedContents, ct);
            item.AssociatedContents.Clear();
            item.AssociatedContents.AddRange(contents);
        }

        // Child-only changes would not UPDATE the items row, and then xmin would not be checked.
        db.Entry(item).Property(i => i.TargetAudience).IsModified = true;
        await db.SaveChangesAsync(ct);
        return await GetDtoAsync(id, userId, ct);
    }

    public async Task DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var item = await db.Items.SingleOrDefaultAsync(i => i.Id == id, ct)
            ?? throw ApiException.NotFound("Item not found");
        if (item.CreatorId != userId)
        {
            throw ApiException.Forbidden("Not authorized to delete this item");
        }

        // Friendly messages for the common cases. The RESTRICT foreign keys still
        // enforce both rules if a visit or a purchase appears between check and delete.
        var usedByVisit = await db.VisitStops.AnyAsync(s => s.ItemId == id, ct)
            || await db.VisitBlockItems.AnyAsync(b => b.ItemId == id, ct);
        if (usedByVisit)
        {
            throw ApiException.Conflict("Items used by a visit cannot be deleted");
        }
        if (await db.Purchases.AnyAsync(p => p.ItemId == id, ct))
        {
            throw ApiException.Conflict("Items that have been purchased cannot be deleted");
        }

        db.Items.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    public async Task<ItemDto> GetDtoAsync(long id, long? viewerId, CancellationToken ct)
    {
        var item = await reader.WithDetails().SingleAsync(i => i.Id == id, ct);
        return await reader.ToDtoAsync(item, viewerId, ct);
    }

    private static List<ItemText> ToTexts(List<DescriptionRequest> descriptions) =>
        descriptions.Select(d => new ItemText { Tone = d.Tone!.Value, Text = d.Text }).ToList();

    /// <summary>Associated contents must be other contents of the same museum.</summary>
    private async Task<List<Content>> AssociatedContentsAsync(Content main, List<long> ids, CancellationToken ct)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Contains(main.Id))
        {
            throw ApiException.BadRequest("The main content cannot also be an associated content");
        }

        var contents = await db.Contents
            .Where(c => distinct.Contains(c.Id) && c.MuseumId == main.MuseumId)
            .ToListAsync(ct);
        if (contents.Count != distinct.Count)
        {
            throw ApiException.BadRequest("Associated contents must exist in the same museum");
        }
        return contents;
    }
}
