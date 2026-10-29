namespace ArtAround.Api.Domain;

public class Museum
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Address { get; set; }
    public string? Description { get; set; }
    public string? Website { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ImageUrl { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<FloorPlan> FloorPlans { get; set; } = [];
    public List<PointOfInterest> PointsOfInterest { get; set; } = [];
    public List<Content> Contents { get; set; } = [];
}
