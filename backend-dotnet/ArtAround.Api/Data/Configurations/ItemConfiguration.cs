using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtAround.Api.Data.Configurations;

public class ItemConfiguration : IEntityTypeConfiguration<Item>
{
    public void Configure(EntityTypeBuilder<Item> item)
    {
        item.Property(i => i.TargetAudience).HasMaxLength(20);
        item.Property(i => i.Price).HasDefaultValue(0).ValueGeneratedNever();
        item.Property(i => i.License).HasDefaultValue(License.CcBy).ValueGeneratedNever();
        item.Property(i => i.IsPublic).HasDefaultValue(false).ValueGeneratedNever();
        item.Property(i => i.Version).IsRowVersion();
        item.ToTable(t => t.HasCheckConstraint("items_price_nonnegative", "price >= 0"));

        // Postgres does not index FK columns on its own; EF adds an index per FK.
        item.HasOne(i => i.Content).WithMany()
            .HasForeignKey(i => i.ContentId).OnDelete(DeleteBehavior.Cascade);
        item.HasOne(i => i.Creator).WithMany()
            .HasForeignKey(i => i.CreatorId).OnDelete(DeleteBehavior.NoAction);

        item.HasMany(i => i.Texts).WithOne()
            .HasForeignKey(t => t.ItemId).OnDelete(DeleteBehavior.Cascade);

        item.HasMany(i => i.AssociatedContents).WithMany()
            .UsingEntity(
                "item_associated_contents",
                r => r.HasOne(typeof(Content)).WithMany().HasForeignKey("content_id").OnDelete(DeleteBehavior.Cascade),
                l => l.HasOne(typeof(Item)).WithMany().HasForeignKey("item_id").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("item_id", "content_id");
                    j.HasIndex("content_id");
                });
    }
}

public class ItemTextConfiguration : IEntityTypeConfiguration<ItemText>
{
    public void Configure(EntityTypeBuilder<ItemText> text)
    {
        // Also serves lookups by item_id alone (leftmost prefix).
        text.HasKey(t => new { t.ItemId, t.Tone });
    }
}

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> purchase)
    {
        // Serves "by user", not "by item": that needs the index EF adds for ItemId.
        purchase.HasKey(p => new { p.UserId, p.ItemId });
        purchase.Property(p => p.PurchasedAt).HasDefaultValueSql("now()");
        purchase.ToTable(t => t.HasCheckConstraint("purchases_price_paid_nonnegative", "price_paid >= 0"));

        purchase.HasOne(p => p.User).WithMany()
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.NoAction);
        // A sold item cannot be deleted: buyers keep what they paid for.
        purchase.HasOne(p => p.Item).WithMany(i => i.Purchases)
            .HasForeignKey(p => p.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}
