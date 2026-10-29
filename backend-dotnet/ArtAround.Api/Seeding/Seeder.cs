using System.Text.Json;
using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Infrastructure;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ArtAround.Api.Seeding;

/// <summary>
/// <c>dotnet run -- seed</c>. Two parts with different rules:
/// museums and contents come from data/museums/*.json and are upserted, so their ids
/// survive a reseed; the demo users, items and visits are wiped and recreated.
/// Ids are never reset, so a JWT issued before the reseed cannot point at a new user.
/// </summary>
public class Seeder(AppDbContext db, IPasswordHasher<User> hasher, IConfiguration config, IWebHostEnvironment env,
    ILogger<Seeder> logger)
{
    public const string DemoPassword = "12345678";

    // The demo texts exist in three lengths and two languages; the schema keeps one per tone.
    private const string SeedLength = "30s";
    private const string SeedLanguage = "it";

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp"];

    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    private string DataDirectory => Path.GetFullPath(Path.Combine(env.ContentRootPath, config["Seed:DataDirectory"] ?? "../../data"));

    private string UploadsDirectory => Path.GetFullPath(Path.Combine(env.ContentRootPath,
        config["Frontends:RepositoryRoot"] ?? "../..", "uploads"));

    public async Task SeedAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        foreach (var file in Directory.GetFiles(Path.Combine(DataDirectory, "museums"), "*.json").Order())
        {
            await LoadMuseumAsync(Read<MuseumFile>(file), ct);
        }

        // TRUNCATE ... CASCADE also empties every table that references users:
        // items (and their texts), purchases, visits (and their children), sessions.
        await db.Database.ExecuteSqlRawAsync("TRUNCATE users CASCADE", ct);
        await CreateDemoDataAsync(Read<DemoFile>(Path.Combine(DataDirectory, "demo-visits.json")), ct);

        await tx.CommitAsync(ct);
        logger.LogInformation("Seed complete. Demo users share the password {Password}", DemoPassword);
    }

    /* ---------- museums and contents: upserted ---------- */

    private async Task LoadMuseumAsync(MuseumFile file, CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        var tx = db.Database.CurrentTransaction!.GetDbTransaction();
        var m = file.Museum;
        var slug = Slug.From(m.Name);

        var museumId = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO museums (name, slug, address, description, website, email, phone, image_url)
            VALUES (@Name, @slug, @Address, @Description, @Website, lower(@Email), @Phone, @ImageUrl)
            ON CONFLICT (slug) DO UPDATE
            SET name = EXCLUDED.name, address = EXCLUDED.address, description = EXCLUDED.description,
                website = EXCLUDED.website, email = EXCLUDED.email, phone = EXCLUDED.phone,
                image_url = EXCLUDED.image_url
            RETURNING id
            """, new { m.Name, slug, m.Address, m.Description, m.Website, m.Email, m.Phone, m.ImageUrl }, tx,
            cancellationToken: ct));

        foreach (var f in m.FloorPlans)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO floor_plans (museum_id, floor, label, image_url, north, south, east, west)
                VALUES (@museumId, @Floor, @Label, @ImageUrl, @North, @South, @East, @West)
                ON CONFLICT (museum_id, floor) DO UPDATE
                SET label = EXCLUDED.label, image_url = EXCLUDED.image_url, north = EXCLUDED.north,
                    south = EXCLUDED.south, east = EXCLUDED.east, west = EXCLUDED.west
                """, new { museumId, f.Floor, f.Label, f.ImageUrl, f.Bounds.North, f.Bounds.South, f.Bounds.East, f.Bounds.West },
                tx, cancellationToken: ct));
        }

        // Nothing references points of interest, so replacing them is simpler than diffing.
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM points_of_interest WHERE museum_id = @museumId", new { museumId }, tx, cancellationToken: ct));
        foreach (var p in m.PointsOfInterest)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO points_of_interest (museum_id, type, label, lat, lng, floor)
                VALUES (@museumId, @Type::poi_type, @Label, @Lat, @Lng, @Floor)
                """, new { museumId, p.Type, p.Label, p.Coordinates.Lat, p.Coordinates.Lng, p.Floor }, tx,
                cancellationToken: ct));
        }

        foreach (var c in file.Contents)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO contents (universal_id, museum_id, type, name, author, year, image_url, lat, lng, floor)
                VALUES (@UniversalId, @museumId, @Type::content_type, @Name, @Author, @Year, @imageUrl, @Lat, @Lng, @Floor)
                ON CONFLICT (universal_id) DO UPDATE
                SET museum_id = EXCLUDED.museum_id, type = EXCLUDED.type, name = EXCLUDED.name,
                    author = EXCLUDED.author, year = EXCLUDED.year, image_url = EXCLUDED.image_url,
                    lat = EXCLUDED.lat, lng = EXCLUDED.lng, floor = EXCLUDED.floor
                """,
                new
                {
                    c.UniversalId, museumId, c.Type, c.Name, c.Author, c.Year,
                    imageUrl = c.ImageUrl ?? ConventionalImage(c.UniversalId),
                    c.Coordinates.Lat, c.Coordinates.Lng, c.Floor,
                }, tx, cancellationToken: ct));
        }
        logger.LogInformation("Loaded {Museum}: {Count} contents", m.Name, file.Contents.Count);
    }

    /// <summary>uploads/contents/&lt;universalId&gt;.&lt;ext&gt;, when the config names no image.</summary>
    private string? ConventionalImage(string universalId) =>
        ImageExtensions
            .Where(ext => File.Exists(Path.Combine(UploadsDirectory, "contents", universalId + ext)))
            .Select(ext => $"/uploads/contents/{universalId}{ext}")
            .FirstOrDefault();

    /* ---------- demo users, items, visits: recreated ---------- */

    private async Task CreateDemoDataAsync(DemoFile demo, CancellationToken ct)
    {
        var users = demo.Users.Select(u =>
        {
            var user = new User { Username = u.Username, Email = $"{u.Username}@artaround.local", WalletBalance = u.WalletBalance };
            user.PasswordHash = hasher.HashPassword(user, DemoPassword);
            return user;
        }).ToDictionary(u => u.Username);
        db.Users.AddRange(users.Values);
        await db.SaveChangesAsync(ct);

        // One public item per artwork, with a fixed random seed so prices are the same on every reseed.
        var descriptions = Read<Dictionary<string, List<DescriptionConfig>>>(Path.Combine(DataDirectory, "item-descriptions.json"));
        var random = new Random(42);
        var artworks = await db.Contents.Where(c => c.Type == ContentType.Artwork).OrderBy(c => c.Id).ToListAsync(ct);
        var items = artworks.ToDictionary(c => c.UniversalId, c => new Item
        {
            ContentId = c.Id,
            CreatorId = users[demo.ItemCreator].Id,
            TargetAudience = "tourist",
            Price = random.Next(10) * 5,
            License = License.CcBy,
            IsPublic = true,
            Texts = TextsFor(c, descriptions),
        });
        db.Items.AddRange(items.Values);
        await db.SaveChangesAsync(ct);

        var museumIds = await db.Museums.ToDictionaryAsync(m => m.Slug, m => m.Id, ct);
        foreach (var v in demo.Visits)
        {
            db.Visits.Add(new Visit
            {
                Title = v.Title,
                Slug = Slug.From(v.Title),
                MuseumId = museumIds[v.Museum],
                CreatorId = users[v.Creator].Id,
                Description = v.Description,
                ImageUrl = v.ImageUrl,
                Type = v.Type,
                Length = v.Length,
                IsPublic = v.IsPublic,
                Stops = v.Stops.Select((universalId, i) => new VisitStop
                {
                    Position = i,
                    ItemId = items[universalId].Id,
                    NextDirections = i < v.Stops.Count - 1 ? demo.Directions[i % demo.Directions.Count] : "",
                    PrevDirections = i > 0 ? demo.Directions[(i - 1) % demo.Directions.Count] : "",
                }).ToList(),
                Blocks = (v.Blocks ?? []).Select((b, i) => new VisitBlock
                {
                    Position = i,
                    Type = b.Type,
                    Name = b.BlockName,
                    Items = (b.Items ?? []).Select((universalId, j) => new VisitBlockItem
                    {
                        Position = j,
                        ItemId = items[universalId].Id,
                    }).ToList(),
                    Questions = (b.Questions ?? []).Select((q, j) => new VisitBlockQuestion
                    {
                        Position = j,
                        Prompt = q.Prompt,
                        AnswerType = q.AnswerType,
                        Options = q.Options ?? [],
                        CorrectIndex = q.CorrectIndex,
                    }).ToList(),
                }).ToList(),
            });
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Created {Users} users, {Items} items, {Visits} visits",
            users.Count, items.Count, demo.Visits.Count);
    }

    private static List<ItemText> TextsFor(Content content, Dictionary<string, List<DescriptionConfig>> descriptions)
    {
        if (!descriptions.TryGetValue(content.UniversalId, out var tones))
        {
            throw new InvalidOperationException(
                $"No hand-written description for \"{content.UniversalId}\": add it to data/item-descriptions.json");
        }
        return tones.Select(t => new ItemText
        {
            Tone = t.Tone,
            Text = (t.Texts.FirstOrDefault(x => x.LengthCategory == SeedLength && x.Language == SeedLanguage)
                ?? t.Texts.First()).Text,
        }).ToList();
    }

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidOperationException($"{path} is empty");

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        JsonConventions.Apply(options);
        return options;
    }
}
