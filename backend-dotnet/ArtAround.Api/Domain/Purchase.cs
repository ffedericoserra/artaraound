namespace ArtAround.Api.Domain;

// PK (UserId, ItemId): an item is bought at most once per user.
public class Purchase
{
    public long UserId { get; set; }
    public long ItemId { get; set; }
    public int PricePaid { get; set; }
    public DateTimeOffset PurchasedAt { get; set; }

    public User User { get; set; } = null!;
    public Item Item { get; set; } = null!;
}
