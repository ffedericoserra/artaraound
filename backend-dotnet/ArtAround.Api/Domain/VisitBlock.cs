namespace ArtAround.Api.Domain;

// A section of the visit: a run of artworks, or a set of questions the group answers.
public class VisitBlock
{
    public long Id { get; set; }
    public long VisitId { get; set; }
    public int Position { get; set; }
    public BlockType Type { get; set; }
    public string Name { get; set; } = "";

    public List<VisitBlockItem> Items { get; set; } = [];
    public List<VisitBlockQuestion> Questions { get; set; } = [];
}

// PK (BlockId, Position).
public class VisitBlockItem
{
    public long BlockId { get; set; }
    public int Position { get; set; }
    public long ItemId { get; set; }
}

public class VisitBlockQuestion
{
    public long Id { get; set; }
    public long BlockId { get; set; }
    public int Position { get; set; }
    public string Prompt { get; set; } = "";
    public AnswerType AnswerType { get; set; }
    // text[]: an ordered list owned by the question, never queried on its own.
    public List<string> Options { get; set; } = [];
    public int? CorrectIndex { get; set; }

    public VisitBlock Block { get; set; } = null!;
}
