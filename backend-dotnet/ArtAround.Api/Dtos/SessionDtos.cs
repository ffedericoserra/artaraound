using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ArtAround.Api.Domain;
using ArtAround.Api.Infrastructure;

namespace ArtAround.Api.Dtos;

public record SessionDto(
    [property: JsonPropertyName("_id")] long Id,
    string Code,
    UserRefDto Owner,
    SessionVisitDto VisitId,
    List<ParticipantDto> Participants,
    int CurrentItemIndex,
    int CurrentStepIndex,
    bool IsActive,
    List<ActivityDto> Activities,
    List<MessageDto> Messages,
    List<SectionResponseDto> SectionResponses,
    bool QuizStarted,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int ParticipantCount,
    bool IsOwner);

/// <summary>The visit as embedded in a session: stops by item id, plus blocks and quiz.</summary>
public record SessionVisitDto(
    [property: JsonPropertyName("_id")] long Id,
    string Title,
    string? Description,
    string Slug,
    List<StopDto<long>> Sequence,
    List<BlockDto> Blocks,
    List<QuizQuestionDto> Quiz,
    MuseumRefDto MuseumId);

public record ParticipantDto(
    long UserId,
    string Username,
    DateTimeOffset JoinedAt,
    bool IsActive,
    // Hidden (absent) on other participants' rows unless the caller owns the session.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<QuizAnswerDto>? QuizAnswers,
    int? QuizScore);

public record QuizAnswerDto(int QuestionIndex, int SelectedIndex);

public record ActivityDto(long ParticipantId, string Username, ActivityAction Action, DateTimeOffset Timestamp);

public record MessageDto(long UserId, string Username, string Text, DateTimeOffset Timestamp);

public record SectionResponseDto(
    long SectionId,
    long QuestionId,
    long UserId,
    string Username,
    AnswerType AnswerType,
    string? Text,
    int? SelectedIndex,
    // Only the session owner sees the automatic correction.
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsCorrect,
    DateTimeOffset SubmittedAt);

public record SessionListItemDto(
    [property: JsonPropertyName("_id")] long Id,
    string Code,
    VisitTitleDto VisitId,
    bool IsActive,
    int ParticipantCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt);

public record VisitTitleDto([property: JsonPropertyName("_id")] long Id, string Title);

/// <summary>A step of the session: an artwork (by its index in the sequence) or a question block.</summary>
public record SessionStep(
    string Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? ItemIndex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? SectionId);

public record NavigationResponse(
    int CurrentItemIndex,
    int CurrentStepIndex,
    SessionStep? Step,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsLast,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsFirst);

public record QuizResultDto(long UserId, string Username, List<QuizAnswerDto> QuizAnswers, int QuizScore, int Total);

public record CreateSessionRequest
{
    [Required(ErrorMessage = "Visit ID is required")]
    public long? VisitId { get; init; }

    [StringLength(50, MinimumLength = 3, ErrorMessage = "Code must be between 3 and 50 characters")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string? Code { get; init; }
}

public record LogActivityRequest : IValidatableObject
{
    [Required(ErrorMessage = "Action is required")]
    public ActivityAction? Action { get; init; }

    // 'joined' and 'left' are written by the server; a client must not forge them.
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Action is ActivityAction.Joined or ActivityAction.Left)
        {
            yield return new ValidationResult("This action is recorded by the server", ["action"]);
        }
    }
}

public record SendMessageRequest
{
    [Required(ErrorMessage = "Message cannot be empty")]
    [StringLength(500, ErrorMessage = "Message must be at most 500 characters")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Text { get; init; } = "";
}

public record SubmitQuizRequest
{
    [Required]
    [MinLength(1, ErrorMessage = "At least one answer is required")]
    public List<QuizAnswerRequest> Answers { get; init; } = [];
}

public record QuizAnswerRequest
{
    [Range(0, int.MaxValue)] public int QuestionIndex { get; init; }
    [Range(0, int.MaxValue)] public int SelectedIndex { get; init; }
}

public record SectionAnswerRequest : IValidatableObject
{
    [Required(ErrorMessage = "Question ID is required")]
    public long? QuestionId { get; init; }

    [Range(0, int.MaxValue)]
    public int? SelectedIndex { get; init; }

    [StringLength(1000, ErrorMessage = "Answer must be at most 1000 characters")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string? Text { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (SelectedIndex is null && string.IsNullOrEmpty(Text))
        {
            yield return new ValidationResult("An answer is required", ["text"]);
        }
    }
}

/// <summary>The "session:state" event: everything a joining client needs to catch up.</summary>
public record SessionStateDto(
    int CurrentItemIndex,
    List<ParticipantDto> Participants,
    int ParticipantCount,
    List<MessageDto> Messages,
    List<ActivityDto> Activities,
    bool QuizStarted,
    int CurrentStepIndex,
    List<SectionResponseDto> SectionResponses);
