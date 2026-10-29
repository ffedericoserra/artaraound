using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtAround.Api.Data.Configurations;

public class VisitConfiguration : IEntityTypeConfiguration<Visit>
{
    public void Configure(EntityTypeBuilder<Visit> visit)
    {
        // Not unique: two visits may share a title. Lookups by slug take the oldest.
        visit.HasIndex(v => v.Slug);
        // ValueGeneratedNever keeps the DEFAULT in the schema (for raw SQL inserts) but makes
        // EF always send the value. Otherwise EF skips a column whose value is the CLR default
        // (false, 0, the first enum member) and the DEFAULT wins: IsPublic = false would
        // become true, Length = Quick would become Normal. The same applies in every
        // configuration below.
        visit.Property(v => v.Type).HasDefaultValue(VisitType.Standard).ValueGeneratedNever();
        visit.Property(v => v.Length).HasDefaultValue(VisitLength.Normal).ValueGeneratedNever();
        visit.Property(v => v.IsPublic).HasDefaultValue(true).ValueGeneratedNever();
        visit.Property(v => v.ViewCount).HasDefaultValue(0).ValueGeneratedNever();
        visit.ToTable(t => t.HasCheckConstraint("visits_view_count_nonnegative", "view_count >= 0"));

        visit.HasOne(v => v.Museum).WithMany()
            .HasForeignKey(v => v.MuseumId).OnDelete(DeleteBehavior.NoAction);
        visit.HasOne(v => v.Creator).WithMany()
            .HasForeignKey(v => v.CreatorId).OnDelete(DeleteBehavior.NoAction);

        visit.HasMany(v => v.Stops).WithOne()
            .HasForeignKey(s => s.VisitId).OnDelete(DeleteBehavior.Cascade);
        visit.HasMany(v => v.Blocks).WithOne()
            .HasForeignKey(b => b.VisitId).OnDelete(DeleteBehavior.Cascade);
        visit.HasMany(v => v.Quiz).WithOne()
            .HasForeignKey(q => q.VisitId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VisitStopConfiguration : IEntityTypeConfiguration<VisitStop>
{
    public void Configure(EntityTypeBuilder<VisitStop> stop)
    {
        stop.HasKey(s => new { s.VisitId, s.Position });
        stop.Property(s => s.NextDirections).HasDefaultValue("").ValueGeneratedNever();
        stop.Property(s => s.PrevDirections).HasDefaultValue("").ValueGeneratedNever();
        stop.ToTable(t => t.HasCheckConstraint("visit_stops_position_nonnegative", "position >= 0"));

        // RESTRICT: an item used by a visit cannot be deleted.
        stop.HasOne(s => s.Item).WithMany()
            .HasForeignKey(s => s.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VisitBlockConfiguration : IEntityTypeConfiguration<VisitBlock>
{
    public void Configure(EntityTypeBuilder<VisitBlock> block)
    {
        block.HasIndex(b => new { b.VisitId, b.Position }).IsUnique();

        block.HasMany(b => b.Items).WithOne()
            .HasForeignKey(i => i.BlockId).OnDelete(DeleteBehavior.Cascade);
        block.HasMany(b => b.Questions).WithOne(q => q.Block)
            .HasForeignKey(q => q.BlockId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class VisitBlockItemConfiguration : IEntityTypeConfiguration<VisitBlockItem>
{
    public void Configure(EntityTypeBuilder<VisitBlockItem> blockItem)
    {
        blockItem.HasKey(i => new { i.BlockId, i.Position });
        blockItem.HasOne<Item>().WithMany()
            .HasForeignKey(i => i.ItemId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class VisitBlockQuestionConfiguration : IEntityTypeConfiguration<VisitBlockQuestion>
{
    public void Configure(EntityTypeBuilder<VisitBlockQuestion> question)
    {
        question.HasIndex(q => new { q.BlockId, q.Position }).IsUnique();
        question.Property(q => q.Options).HasDefaultValueSql("'{}'");
        question.ToTable(t => t.HasCheckConstraint(
            "visit_block_questions_correct_index_in_range",
            "correct_index IS NULL OR (correct_index >= 0 AND correct_index < cardinality(options))"));
    }
}

public class VisitQuizQuestionConfiguration : IEntityTypeConfiguration<VisitQuizQuestion>
{
    public void Configure(EntityTypeBuilder<VisitQuizQuestion> question)
    {
        question.ToTable("visit_quiz_questions", t => t.HasCheckConstraint(
            "visit_quiz_questions_correct_index_in_range",
            "correct_index >= 0 AND correct_index < cardinality(options)"));
        question.HasKey(q => new { q.VisitId, q.Position });
    }
}
