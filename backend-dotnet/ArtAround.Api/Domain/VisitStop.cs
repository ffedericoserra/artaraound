namespace ArtAround.Api.Domain;

// One stop of the visit's sequence. PK (VisitId, Position).
public class VisitStop
{
    public long VisitId { get; set; }
    public int Position { get; set; }
    public long ItemId { get; set; }
    public string NextDirections { get; set; } = "";
    public string PrevDirections { get; set; } = "";
    public string? OverrideImage { get; set; }

    public Item Item { get; set; } = null!;
}
