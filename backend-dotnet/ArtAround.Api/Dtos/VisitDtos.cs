using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ArtAround.Api.Domain;
using ArtAround.Api.Infrastructure;

namespace ArtAround.Api.Dtos;

/// <summary>One stop. TItem is the item as embedded: an ItemDto, an ItemRefDto or a bare id.</summary>
public record StopDto<TItem>(
    TItem ItemId,
    int Order,
    string NextDirections,
    string PrevDirections,
    string? OverrideImage);

public record BlockDto(
    [property: JsonPropertyName("_id")] long Id,
    BlockType Type,
    string BlockName,
    List<long> Items,
    List<BlockQuestionDto> Questions);

public record BlockQuestionDto(
    [property: JsonPropertyName("_id")] long Id,
    string Prompt,
    AnswerType AnswerType,
    List<string> Options,
    // Null (and so absent) when the caller may not see the answer key.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CorrectIndex);

public record QuizQuestionDto(
    string Question,
    List<string> Options,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CorrectIndex);

/// <summary>Visit lists: refs instead of full museum and items, no blocks or quiz.</summary>
public record VisitSummaryDto(
    [property: JsonPropertyName("_id")] long Id,
    string Title,
    string Slug,
    string? Description,
    string? ImageUrl,
    MuseumRefDto MuseumId,
    UserRefDto CreatorId,
    List<StopDto<ItemRefDto>> Sequence,
    VisitType Type,
    VisitLength Length,
    bool IsPublic,
    int ViewCount,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>GET /visits/:id and write responses: everything needed to run the visit.</summary>
public record VisitDetailDto(
    [property: JsonPropertyName("_id")] long Id,
    string Title,
    string Slug,
    string? Description,
    string? ImageUrl,
    MuseumDto MuseumId,
    UserRefDto CreatorId,
    List<StopDto<ItemDto>> Sequence,
    List<BlockDto> Blocks,
    List<QuizQuestionDto> Quiz,
    VisitType Type,
    VisitLength Length,
    bool IsPublic,
    int ViewCount,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record VisitWriteResponse(
    VisitDetailDto Visit, int ChargedAmount, int WalletBalance, List<long> AdoptedItemIds);

public record StopRequest
{
    [Required(ErrorMessage = "Item ID is required")]
    public long? ItemId { get; init; }

    public string NextDirections { get; init; } = "";
    public string PrevDirections { get; init; } = "";

    [Url(ErrorMessage = "overrideImage must be a URL")]
    public string? OverrideImage { get; init; }
}

public record QuestionRequest : IValidatableObject
{
    [Required(ErrorMessage = "Question text is required")]
    [StringLength(1000, ErrorMessage = "Question text must be at most 1000 characters")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Prompt { get; init; } = "";

    [Required(ErrorMessage = "Answer type is required")]
    public AnswerType? AnswerType { get; init; }

    public List<string> Options { get; init; } = [];

    [Range(0, int.MaxValue)]
    public int? CorrectIndex { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Options.Any(o => string.IsNullOrWhiteSpace(o) || o.Trim().Length > 300))
        {
            yield return new ValidationResult("Options must be 1 to 300 characters", ["options"]);
        }
        if (AnswerType != Domain.AnswerType.MultipleChoice)
        {
            yield break;
        }
        if (Options.Count < 2)
        {
            yield return new ValidationResult("Multiple-choice questions require at least 2 options", ["options"]);
        }
        if (CorrectIndex is null || CorrectIndex >= Options.Count)
        {
            yield return new ValidationResult("Select a valid correct option", ["correctIndex"]);
        }
    }
}

public record BlockRequest : IValidatableObject
{
    public BlockType Type { get; init; } = BlockType.Artwork;

    [JsonConverter(typeof(TrimmedStringConverter))]
    public string BlockName { get; init; } = "Mainboard";

    public List<long> Items { get; init; } = [];
    public List<QuestionRequest> Questions { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Type == BlockType.Questions && Questions.Count == 0)
        {
            yield return new ValidationResult("Question sections require at least one question", ["questions"]);
        }
    }
}

public record QuizQuestionRequest : IValidatableObject
{
    [Required(ErrorMessage = "Question is required")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Question { get; init; } = "";

    [MinLength(2, ErrorMessage = "At least 2 options are required")]
    public List<string> Options { get; init; } = [];

    [Range(0, int.MaxValue)]
    public int CorrectIndex { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (CorrectIndex >= Options.Count)
        {
            yield return new ValidationResult("Select a valid correct option", ["correctIndex"]);
        }
    }
}

public record CreateVisitRequest
{
    [Required(ErrorMessage = "Visit title is required")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Title { get; init; } = "";

    [Required(ErrorMessage = "Museum ID is required")]
    public long? MuseumId { get; init; }

    [JsonConverter(typeof(TrimmedStringConverter))]
    public string? Description { get; init; }

    [Url(ErrorMessage = "imageUrl must be a URL")]
    public string? ImageUrl { get; init; }

    public List<StopRequest> Sequence { get; init; } = [];
    public List<BlockRequest> Blocks { get; init; } = [];
    public VisitType Type { get; init; } = VisitType.Standard;
    public VisitLength Length { get; init; } = VisitLength.Normal;
    public bool IsPublic { get; init; } = true;
    public List<QuizQuestionRequest> Quiz { get; init; } = [];
}

/// <summary>PUT /visits/:id. A null property means "leave unchanged"; a list replaces the old one.</summary>
public record UpdateVisitRequest
{
    [MinLength(1, ErrorMessage = "Visit title is required")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string? Title { get; init; }

    [JsonConverter(typeof(TrimmedStringConverter))]
    public string? Description { get; init; }

    [Url(ErrorMessage = "imageUrl must be a URL")]
    public string? ImageUrl { get; init; }

    public List<StopRequest>? Sequence { get; init; }
    public List<BlockRequest>? Blocks { get; init; }
    public VisitType? Type { get; init; }
    public VisitLength? Length { get; init; }
    public bool? IsPublic { get; init; }
    public List<QuizQuestionRequest>? Quiz { get; init; }
}
