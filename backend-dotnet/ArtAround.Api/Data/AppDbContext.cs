using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace ArtAround.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Museum> Museums => Set<Museum>();
    public DbSet<FloorPlan> FloorPlans => Set<FloorPlan>();
    public DbSet<PointOfInterest> PointsOfInterest => Set<PointOfInterest>();
    public DbSet<Content> Contents => Set<Content>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemText> ItemTexts => Set<ItemText>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitStop> VisitStops => Set<VisitStop>();
    public DbSet<VisitBlock> VisitBlocks => Set<VisitBlock>();
    public DbSet<VisitBlockItem> VisitBlockItems => Set<VisitBlockItem>();
    public DbSet<VisitBlockQuestion> VisitBlockQuestions => Set<VisitBlockQuestion>();
    public DbSet<VisitQuizQuestion> VisitQuizQuestions => Set<VisitQuizQuestion>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionParticipant> SessionParticipants => Set<SessionParticipant>();
    public DbSet<SessionQuizAnswer> SessionQuizAnswers => Set<SessionQuizAnswer>();
    public DbSet<SessionActivity> SessionActivities => Set<SessionActivity>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();
    public DbSet<SectionResponse> SectionResponses => Set<SectionResponse>();

    /// <summary>
    /// Registers every C# enum as a Postgres ENUM, at both levels: the Npgsql driver (so
    /// parameters are sent as the enum type) and EF (so migrations emit CREATE TYPE).
    /// </summary>
    public static void MapEnums(NpgsqlDbContextOptionsBuilder npgsql)
    {
        npgsql.MapEnum<Language>("language");
        npgsql.MapEnum<Tone>("tone");
        npgsql.MapEnum<License>("license");
        npgsql.MapEnum<ContentType>("content_type");
        npgsql.MapEnum<PoiType>("poi_type");
        npgsql.MapEnum<VisitType>("visit_type");
        npgsql.MapEnum<VisitLength>("visit_length");
        npgsql.MapEnum<BlockType>("block_type");
        npgsql.MapEnum<AnswerType>("answer_type");
        npgsql.MapEnum<ActivityAction>("activity_action");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // GENERATED ALWAYS AS IDENTITY: the database alone assigns ids.
        modelBuilder.UseIdentityAlwaysColumns();
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // created_at / updated_at on every table that has them. The database owns both:
        // a DEFAULT on insert, the set_updated_at trigger on update (see the first
        // migration). EF only reads them back, so raw SQL and Dapper writes stay correct.
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            if (entity.FindProperty("CreatedAt") is not null)
            {
                modelBuilder.Entity(entity.ClrType).Property("CreatedAt")
                    .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            }
            if (entity.FindProperty("UpdatedAt") is not null)
            {
                modelBuilder.Entity(entity.ClrType).Property("UpdatedAt")
                    .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();
            }
        }
    }
}
