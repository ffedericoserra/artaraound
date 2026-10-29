using System.Data;
using ArtAround.Api.Data;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArtAround.Api.Services;

/// <summary>
/// Buying items with wallet credits. Written in SQL with Dapper because every statement
/// matters: which rows are locked, in which order, and what is re-read after the lock.
///
/// Node did a read-check-write with no transaction, so two concurrent purchases could
/// both pass the balance check. Here the buyer's row is locked FOR UPDATE first, so a
/// second purchase by the same buyer waits, then re-reads the balance and the purchases
/// the first one committed (READ COMMITTED takes a fresh snapshot per statement).
/// </summary>
public class PurchaseService(AppDbContext db)
{
    private record ItemRow(long Id, long CreatorId, int Price, bool IsPublic);

    private record UserRow(long Id, int WalletBalance);

    /// <summary>POST /items/:id/purchase: one item, in its own transaction.</summary>
    public async Task<PurchaseResult> PurchaseAsync(long buyerId, long itemId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await AdoptAsync(buyerId, [itemId], ct);
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// Buys every item in <paramref name="itemIds"/> the buyer neither created nor already
    /// owns, and credits each creator. Must run inside the caller's transaction, so that a
    /// visit save and its charge commit or roll back together.
    /// </summary>
    public async Task<PurchaseResult> AdoptAsync(long buyerId, IEnumerable<long> itemIds, CancellationToken ct)
    {
        var tx = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("AdoptAsync needs an open transaction");
        var conn = tx.Connection!;
        var ids = itemIds.Distinct().ToArray();

        // FOR SHARE: a concurrent price change waits until we commit, so we charge
        // exactly the price stored when the purchase happened.
        var items = (await conn.QueryAsync<ItemRow>(new CommandDefinition("""
            SELECT id, creator_id AS CreatorId, price, is_public AS IsPublic
            FROM items
            WHERE id = ANY(@ids)
            FOR SHARE
            """, new { ids }, tx, cancellationToken: ct))).ToList();
        if (items.Count != ids.Length)
        {
            throw ApiException.BadRequest("One or more items do not exist");
        }

        // Lock the buyer and every creator in id order. Two buyers buying each other's
        // items would otherwise lock the same pair of rows in opposite orders: a deadlock.
        var userIds = items.Select(i => i.CreatorId).Append(buyerId).Distinct().ToArray();
        var users = (await conn.QueryAsync<UserRow>(new CommandDefinition("""
            SELECT id, wallet_balance AS WalletBalance
            FROM users
            WHERE id = ANY(@userIds)
            ORDER BY id
            FOR UPDATE
            """, new { userIds }, tx, cancellationToken: ct))).ToDictionary(u => u.Id);
        var buyer = users.GetValueOrDefault(buyerId) ?? throw ApiException.NotFound("User not found");

        // Read only now, after the lock: a purchase that committed while we waited is visible.
        var owned = (await conn.QueryAsync<long>(new CommandDefinition("""
            SELECT item_id
            FROM purchases
            WHERE user_id = @buyerId AND item_id = ANY(@ids)
            """, new { buyerId, ids }, tx, cancellationToken: ct))).ToHashSet();

        var adoptable = items.Where(i => i.CreatorId != buyerId && !owned.Contains(i.Id)).ToList();
        if (adoptable.Count == 0)
        {
            return new PurchaseResult(0, buyer.WalletBalance, []);
        }
        if (adoptable.Any(i => !i.IsPublic))
        {
            throw ApiException.Forbidden("A private item cannot be adopted");
        }

        var total = adoptable.Sum(i => i.Price);
        if (buyer.WalletBalance < total)
        {
            throw ApiException.BadRequest("Insufficient balance");
        }

        // unnest() turns parallel arrays into rows: one INSERT for any number of items.
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO purchases (user_id, item_id, price_paid)
            SELECT @buyerId, item_id, price
            FROM unnest(@itemIds, @prices) AS bought(item_id, price)
            """,
            new
            {
                buyerId,
                itemIds = adoptable.Select(i => i.Id).ToArray(),
                prices = adoptable.Select(i => i.Price).ToArray(),
            }, tx, cancellationToken: ct));

        var credits = adoptable.GroupBy(i => i.CreatorId).Select(g => (CreatorId: g.Key, Amount: g.Sum(i => i.Price))).ToList();
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE users AS u
            SET wallet_balance = u.wallet_balance + credit.amount
            FROM unnest(@creatorIds, @amounts) AS credit(user_id, amount)
            WHERE u.id = credit.user_id
            """,
            new
            {
                creatorIds = credits.Select(c => c.CreatorId).ToArray(),
                amounts = credits.Select(c => c.Amount).ToArray(),
            }, tx, cancellationToken: ct));

        var balance = await conn.ExecuteScalarAsync<int>(new CommandDefinition("""
            UPDATE users
            SET wallet_balance = wallet_balance - @total
            WHERE id = @buyerId
            RETURNING wallet_balance
            """, new { total, buyerId }, tx, cancellationToken: ct));

        return new PurchaseResult(total, balance, adoptable.Select(i => i.Id).ToList());
    }
}
