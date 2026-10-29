namespace ArtAround.Api.Domain;

public class Visit
{
    public long Id { get; set; }
    public long MuseumId { get; set; }
    public long CreatorId { get; set; }
    public string Title { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public VisitType Type { get; set; } = VisitType.Standard;
    public VisitLength Length { get; set; } = VisitLength.Normal;
    public bool IsPublic { get; set; } = true;
    public int ViewCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Museum Museum { get; set; } = null!;
    public User Creator { get; set; } = null!;

    // The three child collections are owned by the visit: a PUT replaces them whole.
    public List<VisitStop> Stops { get; set; } = [];
    public List<VisitBlock> Blocks { get; set; } = [];
    public List<VisitQuizQuestion> Quiz { get; set; } = [];
}
