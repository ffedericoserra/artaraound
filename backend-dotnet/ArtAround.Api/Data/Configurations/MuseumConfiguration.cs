using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtAround.Api.Data.Configurations;

public class MuseumConfiguration : IEntityTypeConfiguration<Museum>
{
    public void Configure(EntityTypeBuilder<Museum> museum)
    {
        museum.HasIndex(m => m.Slug).IsUnique();
        museum.ToTable(t => t.HasCheckConstraint(
            "slug_has_only_allowed_chars", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'"));

        museum.HasMany(m => m.FloorPlans).WithOne()
            .HasForeignKey(f => f.MuseumId).OnDelete(DeleteBehavior.Cascade);
        museum.HasMany(m => m.PointsOfInterest).WithOne()
            .HasForeignKey(p => p.MuseumId).OnDelete(DeleteBehavior.Cascade);
        museum.HasMany(m => m.Contents).WithOne(c => c.Museum)
            .HasForeignKey(c => c.MuseumId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class FloorPlanConfiguration : IEntityTypeConfiguration<FloorPlan>
{
    public void Configure(EntityTypeBuilder<FloorPlan> floorPlan)
    {
        floorPlan.HasKey(f => new { f.MuseumId, f.Floor });
        floorPlan.ToTable(t =>
        {
            t.HasCheckConstraint("floor_plans_lat_order", "south < north");
            t.HasCheckConstraint("floor_plans_lng_order", "west < east");
            t.HasCheckConstraint("floor_plans_lat_range", "south >= -90 AND north <= 90");
            t.HasCheckConstraint("floor_plans_lng_range", "west >= -180 AND east <= 180");
        });
    }
}

public class PointOfInterestConfiguration : IEntityTypeConfiguration<PointOfInterest>
{
    public void Configure(EntityTypeBuilder<PointOfInterest> poi)
    {
        poi.Property(p => p.Floor).HasDefaultValue(0).ValueGeneratedNever();
        poi.ToTable(t =>
        {
            t.HasCheckConstraint("points_of_interest_lat_range", "lat BETWEEN -90 AND 90");
            t.HasCheckConstraint("points_of_interest_lng_range", "lng BETWEEN -180 AND 180");
        });

        // A point of interest sits on a floor the museum actually has.
        poi.HasOne<FloorPlan>().WithMany()
            .HasForeignKey(p => new { p.MuseumId, p.Floor })
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ContentConfiguration : IEntityTypeConfiguration<Content>
{
    public void Configure(EntityTypeBuilder<Content> content)
    {
        content.HasIndex(c => c.UniversalId).IsUnique();
        // Serves "contents of museum X" and "... of type Y" (leftmost prefix).
        content.HasIndex(c => new { c.MuseumId, c.Type });
        content.Property(c => c.Floor).HasDefaultValue(0).ValueGeneratedNever();
        content.ToTable(t =>
        {
            t.HasCheckConstraint("contents_lat_range", "lat BETWEEN -90 AND 90");
            t.HasCheckConstraint("contents_lng_range", "lng BETWEEN -180 AND 180");
        });

        content.HasOne<FloorPlan>().WithMany()
            .HasForeignKey(c => new { c.MuseumId, c.Floor })
            .OnDelete(DeleteBehavior.NoAction);
    }
}
