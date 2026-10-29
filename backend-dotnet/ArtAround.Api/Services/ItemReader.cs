using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Dtos;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Services;

/// <summary>
/// Turns items into ItemDtos for a given viewer. The per-viewer fields (isOwned,
/// isPurchased) and the sales figures come from two extra queries for the whole batch,
/// never one query per item: N items cost 3 queries, not 2N+1.
/// </summary>
public class ItemReader(AppDbContext db)
{
    /// <summary>Items with everything an ItemDto needs. Split: four collections would multiply rows.</summary>
    public IQueryable<Item> WithDetails() => db.Items
        .AsNoTracking()
        .AsSplitQuery()
        .Include(i => i.Content)
        .Include(i => i.Creator)
        .Include(i => i.Texts)
        .Include(i => i.AssociatedContents);

    public async Task<List<ItemDto>> ToDtosAsync(IReadOnlyCollection<Item> items, long? viewerId, CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).Distinct().ToList();

        // SELECT item_id, count(*), sum(price_paid) FROM purchases WHERE item_id = ANY(@ids) GROUP BY item_id
        var sales = await db.Purchases
            .Where(p => ids.Contains(p.ItemId))
            .GroupBy(p => p.ItemId)
            .Select(g => new { ItemId = g.Key, Count = g.Count(), Revenue = g.Sum(p => p.PricePaid) })
            .ToDictionaryAsync(s => s.ItemId, ct);

        var purchased = viewerId is null
            ? []
            : await db.Purchases
                .Where(p => p.UserId == viewerId && ids.Contains(p.ItemId))
                .Select(p => p.ItemId)
                .ToHashSetAsync(ct);

        return items.Select(item =>
        {
            var isOwned = item.CreatorId == viewerId;
            var isPurchased = purchased.Contains(item.Id);
            var stats = sales.GetValueOrDefault(item.Id);
            return new ItemDto(
                item.Id,
                item.Content.UniversalId,
                new UserRefDto(item.Creator.Id, item.Creator.Username),
                item.TargetAudience,
                item.Texts.OrderBy(t => t.Tone).Select(t => new DescriptionDto(t.Tone, t.Text)).ToList(),
                item.Price,
                item.License,
                item.IsPublic,
                stats?.Count ?? 0,
                isOwned ? stats?.Revenue ?? 0 : null,
                item.AssociatedContents.OrderBy(c => c.Name, StringComparer.Ordinal).Select(ContentDto.From).ToList(),
                item.CreatedAt,
                item.UpdatedAt,
                isOwned,
                isPurchased,
                isOwned || isPurchased ? 0 : item.Price);
        }).ToList();
    }

    public async Task<ItemDto> ToDtoAsync(Item item, long? viewerId, CancellationToken ct) =>
        (await ToDtosAsync([item], viewerId, ct))[0];
}
