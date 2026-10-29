namespace ArtAround.Api.Domain;

// A group visit run live by its owner.
public class Session
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public long OwnerId { get; set; }
    public long VisitId { get; set; }
    public int CurrentItemIndex { get; set; }
    public int CurrentStepIndex { get; set; }
    public bool IsActive { get; set; } = true;
    public bool QuizStarted { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Two "advance" clicks must not both read step 3 and both write step 4.
    public uint Version { get; set; }

    public User Owner { get; set; } = null!;
    public Visit Visit { get; set; } = null!;
    public List<SessionParticipant> Participants { get; set; } = [];
    public List<SessionActivity> Activities { get; set; } = [];
    public List<SessionMessage> Messages { get; set; } = [];
    public List<SectionResponse> SectionResponses { get; set; } = [];
}

// PK (SessionId, UserId).
public class SessionParticipant
{
    public long SessionId { get; set; }
    public long UserId { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public int? QuizScore { get; set; }

    public User User { get; set; } = null!;
    public List<SessionQuizAnswer> QuizAnswers { get; set; } = [];
}

// PK (SessionId, UserId, QuestionIndex).
public class SessionQuizAnswer
{
    public long SessionId { get; set; }
    public long UserId { get; set; }
    public int QuestionIndex { get; set; }
    public int SelectedIndex { get; set; }
}

public class SessionActivity
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long UserId { get; set; }
    public ActivityAction Action { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}

public class SessionMessage
{
    public long Id { get; set; }
    public long SessionId { get; set; }
    public long UserId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public User User { get; set; } = null!;
}

// One participant's answer to one question of a question block.
// PK (SessionId, QuestionId, UserId): answering again overwrites.
public class SectionResponse
{
    public long SessionId { get; set; }
    public long QuestionId { get; set; }
    public long UserId { get; set; }
    public string? Text { get; set; }
    public int? SelectedIndex { get; set; }
    public bool? IsCorrect { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }

    public User User { get; set; } = null!;
    public VisitBlockQuestion Question { get; set; } = null!;
}
