using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Realtime;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Services;

/// <summary>
/// Group visits. Every mutation is a REST call handled here; once it is committed, the
/// notifier broadcasts the result to the session's SignalR group. Sockets never mutate.
/// </summary>
public class SessionService(AppDbContext db, SessionNotifier notifier, TimeProvider clock)
{
    private static readonly string[] Adjectives = ["ROSSO", "BLU", "VERDE", "GIALLO", "VIOLA", "ARANCIO"];
    private static readonly string[] Nouns = ["LEONE", "AQUILA", "TIGRE", "LUPO", "FALCO", "ORSO"];

    /* ---------- reading ---------- */

    /// <summary>The active session with this code. Codes are case-insensitive and stored upper-case.</summary>
    private IQueryable<Session> Active(string code) =>
        db.Sessions.Where(s => s.IsActive && s.Code == code.ToUpperInvariant());

    /// <summary>The latest session with this code, active or ended (owners review results after the end).</summary>
    private IQueryable<Session> Latest(string code) =>
        db.Sessions.Where(s => s.Code == code.ToUpperInvariant()).OrderByDescending(s => s.Id).Take(1);

    private static IQueryable<Session> WithVisitPlan(IQueryable<Session> sessions) => sessions
        .AsSplitQuery()
        .Include(s => s.Visit).ThenInclude(v => v.Stops)
        .Include(s => s.Visit).ThenInclude(v => v.Blocks).ThenInclude(b => b.Items)
        .Include(s => s.Visit).ThenInclude(v => v.Blocks).ThenInclude(b => b.Questions);

    private static IQueryable<Session> WithEverything(IQueryable<Session> sessions) => WithVisitPlan(sessions)
        .AsNoTracking()
        .Include(s => s.Owner)
        .Include(s => s.Visit).ThenInclude(v => v.Museum)
        .Include(s => s.Visit).ThenInclude(v => v.Quiz)
        .Include(s => s.Participants).ThenInclude(p => p.User)
        .Include(s => s.Participants).ThenInclude(p => p.QuizAnswers)
        .Include(s => s.Activities).ThenInclude(a => a.User)
        .Include(s => s.Messages).ThenInclude(m => m.User)
        .Include(s => s.SectionResponses).ThenInclude(r => r.User)
        .Include(s => s.SectionResponses).ThenInclude(r => r.Question);

    public async Task<SessionDto> GetAsync(long userId, string code, CancellationToken ct)
    {
        var session = await WithEverything(Active(code)).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found or inactive");
        return Present(session, userId);
    }

    private async Task<SessionDto> GetByIdAsync(long userId, long id, CancellationToken ct) =>
        Present(await WithEverything(db.Sessions.Where(s => s.Id == id)).SingleAsync(ct), userId);

    /// <summary>The catch-up state a client receives when it joins the session's group.</summary>
    public async Task<(long SessionId, bool IsOwner, SessionStateDto State)?> GetStateAsync(
        long userId, string code, CancellationToken ct)
    {
        var session = await WithEverything(Active(code)).SingleOrDefaultAsync(ct);
        if (session is null)
        {
            return null;
        }
        var dto = Present(session, userId);
        var state = new SessionStateDto(
            dto.CurrentItemIndex, dto.Participants, dto.ParticipantCount, dto.Messages,
            dto.Activities, dto.QuizStarted, dto.CurrentStepIndex, dto.SectionResponses);
        return (session.Id, dto.IsOwner, state);
    }

    public async Task<List<SessionListItemDto>> GetOwnedAsync(long userId, CancellationToken ct) =>
        await db.Sessions
            .AsNoTracking()
            .Where(s => s.OwnerId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .Take(20)
            .Select(s => new SessionListItemDto(
                s.Id, s.Code, new VisitTitleDto(s.Visit.Id, s.Visit.Title), s.IsActive,
                s.Participants.Count(p => p.IsActive), s.StartedAt, s.EndedAt, s.CreatedAt))
            .ToListAsync(ct);

    /// <summary>
    /// Shapes a session for one caller. The host and the visit's author see answer keys;
    /// a participant sees only their own quiz answers and responses, without corrections.
    /// </summary>
    private static SessionDto Present(Session s, long userId)
    {
        var isOwner = s.OwnerId == userId;
        var canSeeKeys = isOwner || s.Visit.CreatorId == userId;
        var visit = s.Visit;

        var visitDto = new SessionVisitDto(
            visit.Id, visit.Title, visit.Description, visit.Slug,
            visit.Stops.OrderBy(st => st.Position)
                .Select(st => new StopDto<long>(st.ItemId, st.Position, st.NextDirections, st.PrevDirections, st.OverrideImage))
                .ToList(),
            VisitService.ToBlockDtos(visit.Blocks, canSeeKeys),
            VisitService.ToQuizDtos(visit.Quiz, canSeeKeys),
            new MuseumRefDto(visit.Museum.Id, visit.Museum.Name, visit.Museum.Slug));

        var participants = s.Participants.OrderBy(p => p.JoinedAt)
            .Select(p => isOwner || p.UserId == userId ? ToDto(p) : ToDto(p) with { QuizAnswers = null, QuizScore = null })
            .ToList();

        var responses = s.SectionResponses
            .Where(r => isOwner || r.UserId == userId)
            .OrderBy(r => r.SubmittedAt)
            .Select(r => ToDto(r, includeCorrection: isOwner))
            .ToList();

        return new SessionDto(
            s.Id, s.Code, new UserRefDto(s.Owner.Id, s.Owner.Username), visitDto, participants,
            s.CurrentItemIndex, s.CurrentStepIndex, s.IsActive,
            s.Activities.OrderBy(a => a.Id).Select(ToDto).ToList(),
            s.Messages.OrderBy(m => m.Id).Select(ToDto).ToList(),
            responses, s.QuizStarted, s.StartedAt, s.EndedAt, s.CreatedAt, s.UpdatedAt,
            s.Participants.Count(p => p.IsActive), isOwner);
    }

    private static ParticipantDto ToDto(SessionParticipant p) => new(
        p.UserId, p.User.Username, p.JoinedAt, p.IsActive,
        p.QuizAnswers.OrderBy(a => a.QuestionIndex).Select(a => new QuizAnswerDto(a.QuestionIndex, a.SelectedIndex)).ToList(),
        p.QuizScore);

    private static ActivityDto ToDto(SessionActivity a) => new(a.UserId, a.User.Username, a.Action, a.CreatedAt);

    private static MessageDto ToDto(SessionMessage m) => new(m.UserId, m.User.Username, m.Text, m.CreatedAt);

    private static SectionResponseDto ToDto(SectionResponse r, bool includeCorrection) => new(
        r.Question.BlockId, r.QuestionId, r.UserId, r.User.Username, r.Question.AnswerType,
        r.Text, r.SelectedIndex, includeCorrection ? r.IsCorrect : null, r.SubmittedAt);

    /// <summary>
    /// The session's steps, in order: each artwork of the sequence, with each non-empty
    /// question block inserted where it sits among the artwork blocks. Without blocks,
    /// the steps are just the sequence.
    /// </summary>
    public static List<SessionStep> BuildSteps(Visit visit)
    {
        var stopCount = visit.Stops.Count;
        var steps = new List<SessionStep>();
        var itemIndex = 0;
        foreach (var block in visit.Blocks.OrderBy(b => b.Position))
        {
            if (block.Type == BlockType.Questions && block.Questions.Count > 0)
            {
                steps.Add(new SessionStep("questions", null, block.Id));
                continue;
            }
            foreach (var _ in block.Items)
            {
                steps.Add(new SessionStep("artwork", itemIndex++, null));
            }
        }
        while (itemIndex < stopCount)
        {
            steps.Add(new SessionStep("artwork", itemIndex++, null));
        }
        return steps;
    }

    /* ---------- lifecycle ---------- */

    public async Task<SessionDto> CreateAsync(long userId, CreateSessionRequest request, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(v => v.Id == request.VisitId, ct)
            ?? throw ApiException.NotFound("Visit not found");
        if (visit.Type != VisitType.Synchronized)
        {
            throw ApiException.BadRequest("Visit is not synchronized");
        }
        // Anyone may host a public synchronized visit; only its author may host a private one.
        if (!visit.IsPublic && visit.CreatorId != userId)
        {
            throw ApiException.Forbidden("Not authorized to host this visit");
        }

        string code;
        if (!string.IsNullOrEmpty(request.Code))
        {
            code = request.Code.ToUpperInvariant();
            if (await Active(code).AnyAsync(ct))
            {
                throw ApiException.Conflict("Session code already in use");
            }
        }
        else
        {
            code = await GenerateFreeCodeAsync(ct);
        }

        // Two requests may both find the code free. The partial unique index on active
        // codes then rejects the second insert, and ApiExceptionHandler answers 409.
        var session = new Session { Code = code, OwnerId = userId, VisitId = visit.Id };
        db.Sessions.Add(session);
        await db.SaveChangesAsync(ct);

        return await GetByIdAsync(userId, session.Id, ct);
    }

    private async Task<string> GenerateFreeCodeAsync(CancellationToken ct)
    {
        string code;
        var attempts = 0;
        do
        {
            code = $"{Random.Shared.GetItems(Adjectives, 1)[0]}_{Random.Shared.GetItems(Nouns, 1)[0]}_{Random.Shared.Next(100)}";
        } while (await Active(code).AnyAsync(ct) && ++attempts < 10);
        return code;
    }

    public async Task<SessionDto> JoinAsync(long userId, string code, CancellationToken ct)
    {
        var session = await Active(code).Include(s => s.Participants).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found or inactive");

        var participant = session.Participants.SingleOrDefault(p => p.UserId == userId);
        if (participant is not null)
        {
            participant.IsActive = true;
        }
        else
        {
            session.Participants.Add(new SessionParticipant { UserId = userId });
            session.Activities.Add(new SessionActivity { UserId = userId, Action = ActivityAction.Joined });
        }
        await db.SaveChangesAsync(ct);

        var dto = await GetByIdAsync(userId, session.Id, ct);
        await BroadcastParticipantsAsync(session.Id, ct);
        return dto;
    }

    public async Task LeaveAsync(long userId, string code, CancellationToken ct)
    {
        var session = await Active(code).Include(s => s.Participants).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found");

        var participant = session.Participants.SingleOrDefault(p => p.UserId == userId);
        if (participant is null)
        {
            return;
        }
        participant.IsActive = false;
        session.Activities.Add(new SessionActivity { UserId = userId, Action = ActivityAction.Left });
        await db.SaveChangesAsync(ct);
        await BroadcastParticipantsAsync(session.Id, ct);
    }

    public async Task EndAsync(long userId, string code, CancellationToken ct)
    {
        var session = await Active(code).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found");
        RequireOwner(session, userId, "Only session owner can end");

        session.IsActive = false;
        session.EndedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await notifier.ToSessionAsync(session.Id, "session:ended", new { });
    }

    private async Task BroadcastParticipantsAsync(long sessionId, CancellationToken ct)
    {
        // Broadcast to the whole room, so quiz answers are left out.
        var participants = await db.SessionParticipants
            .AsNoTracking()
            .Where(p => p.SessionId == sessionId)
            .OrderBy(p => p.JoinedAt)
            .Select(p => new ParticipantDto(p.UserId, p.User.Username, p.JoinedAt, p.IsActive, null, p.QuizScore))
            .ToListAsync(ct);
        await notifier.ToSessionAsync(sessionId, "session:participants", new { participants });
    }

    private static void RequireOwner(Session session, long userId, string message)
    {
        if (session.OwnerId != userId)
        {
            throw ApiException.Forbidden(message);
        }
    }

    /* ---------- navigation ---------- */

    public Task<NavigationResponse> AdvanceAsync(long userId, string code, CancellationToken ct) =>
        MoveAsync(userId, code, forward: true, ct);

    public Task<NavigationResponse> PreviousAsync(long userId, string code, CancellationToken ct) =>
        MoveAsync(userId, code, forward: false, ct);

    /// <summary>
    /// Read-modify-write on current_step_index, guarded by xmin: of two concurrent clicks
    /// one wins and the other gets a 409, rather than both moving from the same step.
    /// </summary>
    private async Task<NavigationResponse> MoveAsync(long userId, string code, bool forward, CancellationToken ct)
    {
        var session = await WithVisitPlan(Active(code)).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found");
        RequireOwner(session, userId, forward ? "Only session owner can advance" : "Only session owner can navigate");

        var steps = BuildSteps(session.Visit);
        var lastIndex = steps.Count - 1;
        var canMove = forward ? session.CurrentStepIndex < lastIndex : session.CurrentStepIndex > 0;
        if (canMove)
        {
            session.CurrentStepIndex += forward ? 1 : -1;
            if (steps[session.CurrentStepIndex].ItemIndex is { } itemIndex)
            {
                session.CurrentItemIndex = itemIndex;
            }
            await db.SaveChangesAsync(ct);
        }

        var step = steps.ElementAtOrDefault(session.CurrentStepIndex);
        bool? isLast = forward ? session.CurrentStepIndex >= lastIndex : null;
        bool? isFirst = forward ? null : session.CurrentStepIndex == 0;
        var response = new NavigationResponse(session.CurrentItemIndex, session.CurrentStepIndex, step, isLast, isFirst);

        await notifier.ToSessionAsync(session.Id, "session:step-changed", response);
        if (step?.ItemIndex is not null)
        {
            await notifier.ToSessionAsync(session.Id, "session:item-changed",
                new { response.CurrentItemIndex, response.IsLast, response.IsFirst });
        }
        return response;
    }

    /* ---------- activity and chat ---------- */

    private async Task<Session> RequireMemberAsync(long userId, string code, CancellationToken ct)
    {
        var session = await Active(code).AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found or inactive");
        var isMember = session.OwnerId == userId
            || await db.SessionParticipants.AnyAsync(p => p.SessionId == session.Id && p.UserId == userId, ct);
        if (!isMember)
        {
            throw ApiException.Forbidden("You are not in this session");
        }
        return session;
    }

    public async Task LogActivityAsync(long userId, string code, ActivityAction action, CancellationToken ct)
    {
        var session = await RequireMemberAsync(userId, code, ct);
        var activity = new SessionActivity { SessionId = session.Id, UserId = userId, Action = action };
        db.SessionActivities.Add(activity);
        await db.SaveChangesAsync(ct);

        var username = await UsernameAsync(userId, ct);
        await notifier.ToSessionAsync(session.Id, "session:activity",
            new ActivityDto(userId, username, action, activity.CreatedAt));
    }

    public async Task<MessageDto> SendMessageAsync(long userId, string code, string text, CancellationToken ct)
    {
        var session = await RequireMemberAsync(userId, code, ct);
        var message = new SessionMessage { SessionId = session.Id, UserId = userId, Text = text };
        db.SessionMessages.Add(message);
        await db.SaveChangesAsync(ct);

        // Sent to everyone, the sender included, so all clients append it the same way.
        var dto = new MessageDto(userId, await UsernameAsync(userId, ct), text, message.CreatedAt);
        await notifier.ToSessionAsync(session.Id, "session:chat", dto);
        return dto;
    }

    private Task<string> UsernameAsync(long userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.Username).SingleAsync(ct);

    /* ---------- question blocks ---------- */

    /// <returns>The participant's view of the response, and whether it was a first answer (201) or a change (200).</returns>
    public async Task<(SectionResponseDto Response, bool Created)> AnswerSectionAsync(
        long userId, string code, long sectionId, SectionAnswerRequest request, CancellationToken ct)
    {
        var session = await WithVisitPlan(Active(code)).AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found or inactive");
        var isActiveParticipant = await db.SessionParticipants
            .AnyAsync(p => p.SessionId == session.Id && p.UserId == userId && p.IsActive, ct);
        if (!isActiveParticipant)
        {
            throw ApiException.Forbidden("Only active participants can answer");
        }

        var current = BuildSteps(session.Visit).ElementAtOrDefault(session.CurrentStepIndex);
        if (current?.SectionId != sectionId)
        {
            throw ApiException.Conflict("This question section is not active");
        }
        var question = session.Visit.Blocks.Single(b => b.Id == sectionId).Questions
            .SingleOrDefault(q => q.Id == request.QuestionId)
            ?? throw ApiException.NotFound("Question not found");

        string? text = null;
        int? selectedIndex = null;
        bool? isCorrect = null;
        if (question.AnswerType == AnswerType.MultipleChoice)
        {
            if (request.SelectedIndex is not { } selected || selected >= question.Options.Count)
            {
                throw ApiException.BadRequest("Select a valid option");
            }
            selectedIndex = selected;
            isCorrect = selected == question.CorrectIndex;
        }
        else
        {
            text = string.IsNullOrEmpty(request.Text) ? throw ApiException.BadRequest("Write an answer") : request.Text;
        }

        // An upsert in one statement, so two quick submissions cannot both insert.
        // xmax is 0 on a freshly inserted row and non-zero on an updated one.
        var created = await db.Database.GetDbConnection().ExecuteScalarAsync<bool>(new CommandDefinition("""
            INSERT INTO section_responses (session_id, question_id, user_id, text, selected_index, is_correct)
            VALUES (@sessionId, @questionId, @userId, @text, @selectedIndex, @isCorrect)
            ON CONFLICT (session_id, question_id, user_id) DO UPDATE
            SET text = EXCLUDED.text,
                selected_index = EXCLUDED.selected_index,
                is_correct = EXCLUDED.is_correct,
                submitted_at = now()
            RETURNING xmax = 0
            """,
            new { sessionId = session.Id, questionId = question.Id, userId, text, selectedIndex, isCorrect },
            cancellationToken: ct));

        var response = new SectionResponseDto(
            sectionId, question.Id, userId, await UsernameAsync(userId, ct), question.AnswerType,
            text, selectedIndex, isCorrect, clock.GetUtcNow());
        // The host reviews corrections live; the participant may still change the answer, so not them.
        await notifier.ToOwnerAsync(session.Id, "session:section-response", response);
        return (response with { IsCorrect = null }, created);
    }

    public async Task<List<SectionResponseDto>> GetSectionResponsesAsync(
        long userId, string code, long sectionId, CancellationToken ct)
    {
        var session = await Latest(code).AsNoTracking().SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found");
        RequireOwner(session, userId, "Only the session owner can view responses");

        var responses = await db.SectionResponses
            .AsNoTracking()
            .Include(r => r.User)
            .Include(r => r.Question)
            .Where(r => r.SessionId == session.Id && r.Question.BlockId == sectionId)
            .OrderBy(r => r.SubmittedAt)
            .ToListAsync(ct);
        return responses.Select(r => ToDto(r, includeCorrection: true)).ToList();
    }

    /* ---------- final quiz ---------- */

    public async Task StartQuizAsync(long userId, string code, CancellationToken ct)
    {
        var session = await Active(code).Include(s => s.Visit.Quiz).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found or inactive");
        RequireOwner(session, userId, "Only session owner can start the quiz");
        if (session.Visit.Quiz.Count == 0)
        {
            throw ApiException.BadRequest("This visit has no quiz");
        }

        session.QuizStarted = true;
        await db.SaveChangesAsync(ct);
        await notifier.ToSessionAsync(session.Id, "session:quiz-started", new { });
    }

    public async Task<(int Score, int Total)> SubmitQuizAsync(
        long userId, string code, List<QuizAnswerRequest> answers, CancellationToken ct)
    {
        var session = await Active(code).AsNoTracking().Include(s => s.Visit.Quiz).SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found or inactive");
        var quiz = session.Visit.Quiz.ToDictionary(q => q.Position);
        if (quiz.Count == 0)
        {
            throw ApiException.BadRequest("This session has no quiz");
        }

        var participant = await db.SessionParticipants
            .SingleOrDefaultAsync(p => p.SessionId == session.Id && p.UserId == userId, ct)
            ?? throw ApiException.Forbidden("You are not a participant of this session");
        if (participant.QuizScore is not null)
        {
            throw ApiException.BadRequest("Quiz already submitted");
        }

        // One answer per question: the last one sent wins.
        var byQuestion = answers.GroupBy(a => a.QuestionIndex).Select(g => g.Last()).ToList();
        var score = byQuestion.Count(a => quiz.TryGetValue(a.QuestionIndex, out var q) && q.CorrectIndex == a.SelectedIndex);

        participant.QuizScore = score;
        db.SessionQuizAnswers.AddRange(byQuestion.Select(a => new SessionQuizAnswer
        {
            SessionId = session.Id,
            UserId = userId,
            QuestionIndex = a.QuestionIndex,
            SelectedIndex = a.SelectedIndex,
        }));
        await db.SaveChangesAsync(ct);

        // The leaderboard needs only the score; the answers stay behind GET .../quiz.
        await notifier.ToSessionAsync(session.Id, "session:quiz-submitted", new
        {
            userId,
            username = await UsernameAsync(userId, ct),
            quizScore = score,
            total = quiz.Count,
        });
        return (score, quiz.Count);
    }

    public async Task<(List<QuizResultDto> Results, List<QuizQuestionDto> Quiz)> GetQuizResultsAsync(
        long userId, string code, CancellationToken ct)
    {
        var session = await Latest(code)
            .AsNoTracking()
            .AsSplitQuery()
            .Include(s => s.Visit.Quiz)
            .Include(s => s.Participants).ThenInclude(p => p.User)
            .Include(s => s.Participants).ThenInclude(p => p.QuizAnswers)
            .SingleOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Session not found");
        RequireOwner(session, userId, "Only session owner can view quiz results");

        var total = session.Visit.Quiz.Count;
        var results = session.Participants
            .Where(p => p.QuizScore is not null)
            .Select(p => new QuizResultDto(
                p.UserId, p.User.Username,
                p.QuizAnswers.OrderBy(a => a.QuestionIndex).Select(a => new QuizAnswerDto(a.QuestionIndex, a.SelectedIndex)).ToList(),
                p.QuizScore!.Value, total))
            .ToList();
        return (results, VisitService.ToQuizDtos(session.Visit.Quiz, showKeys: true));
    }
}
