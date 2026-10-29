using ArtAround.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ArtAround.Api.Data.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> session)
    {
        // Codes are reused once a session ends, so uniqueness only holds among active ones.
        session.HasIndex(s => s.Code).IsUnique().HasFilter("is_active");
        session.Property(s => s.CurrentItemIndex).HasDefaultValue(0).ValueGeneratedNever();
        session.Property(s => s.CurrentStepIndex).HasDefaultValue(0).ValueGeneratedNever();
        session.Property(s => s.IsActive).HasDefaultValue(true).ValueGeneratedNever();
        session.Property(s => s.QuizStarted).HasDefaultValue(false).ValueGeneratedNever();
        session.Property(s => s.StartedAt).HasDefaultValueSql("now()");
        session.Property(s => s.Version).IsRowVersion();
        session.ToTable(t =>
        {
            t.HasCheckConstraint("sessions_code_upper", "code = upper(code)");
            t.HasCheckConstraint("sessions_ended_iff_inactive", "is_active = (ended_at IS NULL)");
        });

        session.HasOne(s => s.Owner).WithMany()
            .HasForeignKey(s => s.OwnerId).OnDelete(DeleteBehavior.NoAction);
        session.HasOne(s => s.Visit).WithMany()
            .HasForeignKey(s => s.VisitId).OnDelete(DeleteBehavior.Cascade);

        session.HasMany(s => s.Participants).WithOne()
            .HasForeignKey(p => p.SessionId).OnDelete(DeleteBehavior.Cascade);
        session.HasMany(s => s.Activities).WithOne()
            .HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
        session.HasMany(s => s.Messages).WithOne()
            .HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        session.HasMany(s => s.SectionResponses).WithOne()
            .HasForeignKey(r => r.SessionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SessionParticipantConfiguration : IEntityTypeConfiguration<SessionParticipant>
{
    public void Configure(EntityTypeBuilder<SessionParticipant> participant)
    {
        participant.HasKey(p => new { p.SessionId, p.UserId });
        participant.Property(p => p.JoinedAt).HasDefaultValueSql("now()");
        participant.Property(p => p.IsActive).HasDefaultValue(true).ValueGeneratedNever();

        participant.HasOne(p => p.User).WithMany()
            .HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.NoAction);
        participant.HasMany(p => p.QuizAnswers).WithOne()
            .HasForeignKey(a => new { a.SessionId, a.UserId }).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SessionQuizAnswerConfiguration : IEntityTypeConfiguration<SessionQuizAnswer>
{
    public void Configure(EntityTypeBuilder<SessionQuizAnswer> answer)
    {
        answer.HasKey(a => new { a.SessionId, a.UserId, a.QuestionIndex });
        answer.ToTable(t => t.HasCheckConstraint(
            "session_quiz_answers_indexes_nonnegative", "question_index >= 0 AND selected_index >= 0"));
    }
}

public class SessionActivityConfiguration : IEntityTypeConfiguration<SessionActivity>
{
    public void Configure(EntityTypeBuilder<SessionActivity> activity)
    {
        activity.HasOne(a => a.User).WithMany()
            .HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class SessionMessageConfiguration : IEntityTypeConfiguration<SessionMessage>
{
    public void Configure(EntityTypeBuilder<SessionMessage> message)
    {
        message.ToTable(t => t.HasCheckConstraint(
            "session_messages_text_length", "char_length(text) BETWEEN 1 AND 500"));
        message.HasOne(m => m.User).WithMany()
            .HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}

public class SectionResponseConfiguration : IEntityTypeConfiguration<SectionResponse>
{
    public void Configure(EntityTypeBuilder<SectionResponse> response)
    {
        response.HasKey(r => new { r.SessionId, r.QuestionId, r.UserId });
        response.Property(r => r.SubmittedAt).HasDefaultValueSql("now()");
        response.ToTable(t =>
        {
            // Exactly one of the two answer forms.
            t.HasCheckConstraint("section_responses_one_answer", "num_nonnulls(text, selected_index) = 1");
            t.HasCheckConstraint("section_responses_text_length", "char_length(text) <= 1000");
        });

        response.HasOne(r => r.User).WithMany()
            .HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.NoAction);
        response.HasOne(r => r.Question).WithMany()
            .HasForeignKey(r => r.QuestionId).OnDelete(DeleteBehavior.Cascade);
    }
}
