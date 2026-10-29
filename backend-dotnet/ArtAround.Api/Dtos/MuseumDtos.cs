using System.Text.Json.Serialization;
using ArtAround.Api.Domain;

namespace ArtAround.Api.Dtos;

// "_id" keeps the Mongo-era contract the frontends were written against.

public record MuseumSummaryDto(
    [property: JsonPropertyName("_id")] long Id,
    string Name,
    string Slug,
    string? Address,
    string? Description,
    string? ImageUrl);

public record MuseumDto(
    [property: JsonPropertyName("_id")] long Id,
    string Name,
    string Slug,
    string? Address,
    string? Description,
    string? Website,
    string? Email,
    string? Phone,
    string? ImageUrl,
    List<FloorPlanDto> FloorPlans,
    List<PointOfInterestDto> PointsOfInterest,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static MuseumDto From(Museum m) => new(
        m.Id, m.Name, m.Slug, m.Address, m.Description, m.Website, m.Email, m.Phone, m.ImageUrl,
        m.FloorPlans.OrderBy(f => f.Floor)
            .Select(f => new FloorPlanDto(f.Floor, f.Label, f.ImageUrl, new BoundsDto(f.North, f.South, f.East, f.West)))
            .ToList(),
        m.PointsOfInterest.OrderBy(p => p.Id)
            .Select(p => new PointOfInterestDto(p.Type, new CoordinatesDto(p.Lat, p.Lng), p.Label, p.Floor))
            .ToList(),
        m.CreatedAt, m.UpdatedAt);
}

/// <summary>A museum as embedded in visits and sessions.</summary>
public record MuseumRefDto([property: JsonPropertyName("_id")] long Id, string Name, string Slug);

public record FloorPlanDto(int Floor, string? Label, string? ImageUrl, BoundsDto Bounds);

public record BoundsDto(double North, double South, double East, double West);

public record PointOfInterestDto(PoiType Type, CoordinatesDto Coordinates, string Label, int Floor);

public record CoordinatesDto(double Lat, double Lng);

public record ContentDto(
    [property: JsonPropertyName("_id")] long Id,
    string UniversalId,
    long MuseumId,
    ContentType Type,
    string Name,
    string? Author,
    string? Year,
    string? ImageUrl,
    CoordinatesDto Coordinates,
    int Floor,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ContentDto From(Content c) => new(
        c.Id, c.UniversalId, c.MuseumId, c.Type, c.Name, c.Author, c.Year, c.ImageUrl,
        new CoordinatesDto(c.Lat, c.Lng), c.Floor, c.CreatedAt, c.UpdatedAt);
}
