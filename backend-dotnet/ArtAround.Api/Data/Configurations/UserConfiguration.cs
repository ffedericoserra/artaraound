using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtAround.Api.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> user)
    {
        user.Property(u => u.Username).HasMaxLength(30);
        user.Property(u => u.Language).HasDefaultValue(Language.It).ValueGeneratedNever();
        user.Property(u => u.WalletBalance).HasDefaultValue(100).ValueGeneratedNever();
        // The case-insensitive unique indexes on lower(username) and lower(email) are
        // expression indexes, which EF cannot model: they are raw SQL in the migration.
        user.ToTable(t =>
        {
            t.HasCheckConstraint("users_username_trimmed", "username = btrim(username)");
            t.HasCheckConstraint("users_username_min_len", "char_length(username) >= 3");
            t.HasCheckConstraint("users_wallet_nonnegative", "wallet_balance >= 0");
        });

        user.HasMany(u => u.SavedVisits).WithMany()
            .UsingEntity(
                "saved_visits",
                r => r.HasOne(typeof(Visit)).WithMany().HasForeignKey("visit_id").OnDelete(DeleteBehavior.Cascade),
                l => l.HasOne(typeof(User)).WithMany().HasForeignKey("user_id").OnDelete(DeleteBehavior.Cascade),
                j =>
                {
                    j.HasKey("user_id", "visit_id");
                    j.HasIndex("visit_id");
                });
    }
}
