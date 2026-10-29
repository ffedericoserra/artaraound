namespace ArtAround.Api.Domain;

public class Item
{
    public long Id { get; set; }
    public long ContentId { get; set; }
    public long CreatorId { get; set; }
    public string TargetAudience { get; set; } = "";
    public int Price { get; set; }
    public License License { get; set; } = License.CcBy;
    public bool IsPublic { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Postgres xmin: optimistic concurrency token for read-modify-write updates.
    public uint Version { get; set; }

    public Content Content { get; set; } = null!;
    public User Creator { get; set; } = null!;
    public List<ItemText> Texts { get; set; } = [];
    public List<Content> AssociatedContents { get; set; } = [];
    public List<Purchase> Purchases { get; set; } = [];
}
