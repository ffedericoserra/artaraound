namespace ArtAround.Api.Domain;

public class Content
{
    public long Id { get; set; }
    public string UniversalId { get; set; } = "";
    public long MuseumId { get; set; }
    public ContentType Type { get; set; }
    public string Name { get; set; } = "";
    public string? Author { get; set; }
    public string? Year { get; set; }
    public string? ImageUrl { get; set; }
    public double Lat { get; set; }
    public double Lng { get; set; }
    public int Floor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Museum Museum { get; set; } = null!;
}
