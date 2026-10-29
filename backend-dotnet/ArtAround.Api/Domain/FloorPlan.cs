namespace ArtAround.Api.Domain;

// PK (MuseumId, Floor): a floor exists once per museum.
public class FloorPlan
{
    public long MuseumId { get; set; }
    public int Floor { get; set; }
    public string? Label { get; set; }
    public string? ImageUrl { get; set; }
    public double North { get; set; }
    public double South { get; set; }
    public double East { get; set; }
    public double West { get; set; }
}
