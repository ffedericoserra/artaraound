using ArtAround.Api.Domain;

namespace ArtAround.Api.Seeding;

// Shapes of the JSON files in data/. Enum-valued fields stay strings here: they are
// cast to the Postgres ENUM in SQL (Dapper would send a C# enum as its integer value).

public record MuseumFile(MuseumConfig Museum, List<ContentConfig> Contents);

public record MuseumConfig(
    string Name,
    string? Address,
    string? Description,
    string? Website,
    string? Email,
    string? Phone,
    string? ImageUrl,
    List<FloorPlanConfig> FloorPlans,
    List<PoiConfig> PointsOfInterest);

public record FloorPlanConfig(int Floor, string? Label, string? ImageUrl, BoundsConfig Bounds);

public record BoundsConfig(double North, double South, double East, double West);

public record PoiConfig(string Type, CoordinatesConfig Coordinates, string Label, int Floor = 0);

public record CoordinatesConfig(double Lat, double Lng);

public record ContentConfig(
    string Type,
    string UniversalId,
    string Name,
    string? Author,
    string? Year,
    string? ImageUrl,
    CoordinatesConfig Coordinates,
    int Floor = 0);

/// <summary>data/item-descriptions.json: universalId → tones → texts (one per length and language).</summary>
public record DescriptionConfig(Tone Tone, List<DescriptionTextConfig> Texts);

public record DescriptionTextConfig(string Text, string LengthCategory, string Language);

public record DemoFile(List<string> Directions, List<DemoUser> Users, string ItemCreator, List<DemoVisit> Visits);

public record DemoUser(string Username, int WalletBalance);

public record DemoVisit(
    string Title,
    string Museum,
    string Creator,
    string Description,
    List<string> Stops,
    VisitType Type,
    VisitLength Length,
    bool IsPublic,
    string? ImageUrl,
    List<DemoBlock>? Blocks);

public record DemoBlock(BlockType Type, string BlockName, List<string>? Items, List<DemoQuestion>? Questions);

public record DemoQuestion(string Prompt, AnswerType AnswerType, List<string>? Options, int? CorrectIndex);
