namespace ArtAround.Api.Domain;

// PK (VisitId, Position). Participants answer by position (questionIndex).
public class VisitQuizQuestion
{
    public long VisitId { get; set; }
    public int Position { get; set; }
    public string Question { get; set; } = "";
    public List<string> Options { get; set; } = [];
    public int CorrectIndex { get; set; }
}
