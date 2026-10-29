using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Services;

public class VisitService(AppDbContext db, ItemReader itemReader, PurchaseService purchases)
{
    /// <summary>Visits are addressed by id or by slug; a slug shared by two visits resolves to the oldest.</summary>
    public async Task<long?> ResolveIdAsync(string idOrSlug, CancellationToken ct)
    {
        if (long.TryParse(idOrSlug, out var id))
        {
            return await db.Visits.AnyAsync(v => v.Id == id, ct) ? id : null;
        }
        return await db.Visits
            .Where(v => v.Slug == idOrSlug)
            .OrderBy(v => v.Id)
            .Select(v => (long?)v.Id)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// A projection, not an Include: EF selects only the columns the DTO needs and builds
    /// the stops list from a join, in one round trip.
    /// </summary>
    public IQueryable<VisitSummaryDto> Summaries(IQueryable<Visit> visits) => visits.Select(v => new VisitSummaryDto(
        v.Id, v.Title, v.Slug, v.Description, v.ImageUrl,
        new MuseumRefDto(v.Museum.Id, v.Museum.Name, v.Museum.Slug),
        new UserRefDto(v.Creator.Id, v.Creator.Username),
        v.Stops.OrderBy(s => s.Position)
            .Select(s => new StopDto<ItemRefDto>(
                new ItemRefDto(s.ItemId, s.Item.Content.UniversalId),
                s.Position, s.NextDirections, s.PrevDirections, s.OverrideImage))
            .ToList(),
        v.Type, v.Length, v.IsPublic, v.ViewCount, v.Stops.Count, v.CreatedAt, v.UpdatedAt));

    /// <summary>The full visit. Answer keys are kept only for the visit's creator.</summary>
    public async Task<VisitDetailDto?> GetDetailAsync(long id, long? viewerId, CancellationToken ct)
    {
        var visit = await db.Visits
            .AsNoTracking()
            .AsSplitQuery()
            .Include(v => v.Museum).ThenInclude(m => m.FloorPlans)
            .Include(v => v.Museum).ThenInclude(m => m.PointsOfInterest)
            .Include(v => v.Creator)
            .Include(v => v.Stops)
            .Include(v => v.Blocks).ThenInclude(b => b.Items)
            .Include(v => v.Blocks).ThenInclude(b => b.Questions)
            .Include(v => v.Quiz)
            .SingleOrDefaultAsync(v => v.Id == id, ct);
        if (visit is null)
        {
            return null;
        }

        var itemIds = visit.Stops.Select(s => s.ItemId).Distinct().ToList();
        var items = await itemReader.WithDetails().Where(i => itemIds.Contains(i.Id)).ToListAsync(ct);
        var itemDtos = (await itemReader.ToDtosAsync(items, viewerId, ct)).ToDictionary(i => i.Id);
        var showKeys = viewerId == visit.CreatorId;

        return new VisitDetailDto(
            visit.Id, visit.Title, visit.Slug, visit.Description, visit.ImageUrl,
            MuseumDto.From(visit.Museum),
            new UserRefDto(visit.Creator.Id, visit.Creator.Username),
            visit.Stops.OrderBy(s => s.Position)
                .Select(s => new StopDto<ItemDto>(itemDtos[s.ItemId], s.Position, s.NextDirections, s.PrevDirections, s.OverrideImage))
                .ToList(),
            ToBlockDtos(visit.Blocks, showKeys),
            ToQuizDtos(visit.Quiz, showKeys),
            visit.Type, visit.Length, visit.IsPublic, visit.ViewCount, visit.Stops.Count,
            visit.CreatedAt, visit.UpdatedAt);
    }

    public static List<BlockDto> ToBlockDtos(IEnumerable<VisitBlock> blocks, bool showKeys) =>
        blocks.OrderBy(b => b.Position)
            .Select(b => new BlockDto(
                b.Id, b.Type, b.Name,
                b.Items.OrderBy(i => i.Position).Select(i => i.ItemId).ToList(),
                b.Questions.OrderBy(q => q.Position)
                    .Select(q => new BlockQuestionDto(q.Id, q.Prompt, q.AnswerType, q.Options, showKeys ? q.CorrectIndex : null))
                    .ToList()))
            .ToList();

    public static List<QuizQuestionDto> ToQuizDtos(IEnumerable<VisitQuizQuestion> quiz, bool showKeys) =>
        quiz.OrderBy(q => q.Position)
            .Select(q => new QuizQuestionDto(q.Question, q.Options, showKeys ? q.CorrectIndex : null))
            .ToList();

    /// <summary>
    /// Creates the visit and buys the sequence's items in one transaction: if the wallet
    /// is short, no visit is saved. (Node charged first and saved after, separately.)
    /// </summary>
    public async Task<VisitWriteResponse> CreateAsync(long userId, CreateVisitRequest request, CancellationToken ct)
    {
        if (!await db.Museums.AnyAsync(m => m.Id == request.MuseumId, ct))
        {
            throw ApiException.NotFound("Museum not found");
        }
        await EnsureBlockItemsExistAsync(request.Blocks, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var billing = await purchases.AdoptAsync(userId, request.Sequence.Select(s => s.ItemId!.Value), ct);

        var visit = new Visit
        {
            MuseumId = request.MuseumId!.Value,
            CreatorId = userId,
            Title = request.Title,
            Slug = Slug.From(request.Title),
            Description = request.Description,
            ImageUrl = request.ImageUrl,
            Type = request.Type,
            Length = request.Length,
            IsPublic = request.IsPublic,
            Stops = ToStops(request.Sequence),
            Blocks = ToBlocks(request.Blocks),
            Quiz = ToQuiz(request.Quiz),
        };
        db.Visits.Add(visit);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var detail = (await GetDetailAsync(visit.Id, userId, ct))!;
        return new VisitWriteResponse(detail, billing.ChargedAmount, billing.WalletBalance, billing.AdoptedItemIds);
    }

    public async Task<VisitWriteResponse> UpdateAsync(long userId, long id, UpdateVisitRequest request, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(v => v.Id == id, ct)
            ?? throw ApiException.NotFound("Visit not found");
        if (visit.CreatorId != userId)
        {
            throw ApiException.Forbidden("Not authorized to update this visit");
        }
        if (request.Blocks is not null)
        {
            await EnsureBlockItemsExistAsync(request.Blocks, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // Items already in the visit were bought when they were added, so adopting the
        // whole new sequence only charges for the new ones.
        var billing = await purchases.AdoptAsync(
            userId, request.Sequence?.Select(s => s.ItemId!.Value) ?? [], ct);

        if (request.Title is not null)
        {
            visit.Title = request.Title;
            visit.Slug = Slug.From(request.Title);
        }
        if (request.Description is not null) visit.Description = request.Description;
        if (request.ImageUrl is not null) visit.ImageUrl = request.ImageUrl;
        if (request.Type is not null) visit.Type = request.Type.Value;
        if (request.Length is not null) visit.Length = request.Length.Value;
        if (request.IsPublic is not null) visit.IsPublic = request.IsPublic.Value;

        // A list in the request replaces the old one: delete the old rows in one statement
        // each (cascading to block items, questions and their answers), then insert anew.
        if (request.Sequence is not null)
        {
            await db.VisitStops.Where(s => s.VisitId == id).ExecuteDeleteAsync(ct);
            visit.Stops.AddRange(ToStops(request.Sequence));
        }
        if (request.Blocks is not null)
        {
            await db.VisitBlocks.Where(b => b.VisitId == id).ExecuteDeleteAsync(ct);
            visit.Blocks.AddRange(ToBlocks(request.Blocks));
        }
        if (request.Quiz is not null)
        {
            await db.VisitQuizQuestions.Where(q => q.VisitId == id).ExecuteDeleteAsync(ct);
            visit.Quiz.AddRange(ToQuiz(request.Quiz));
        }

        // Always UPDATE the visits row, even when only child rows changed, so the
        // set_updated_at trigger bumps updated_at.
        db.Entry(visit).Property(v => v.Title).IsModified = true;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var detail = (await GetDetailAsync(id, userId, ct))!;
        return new VisitWriteResponse(detail, billing.ChargedAmount, billing.WalletBalance, billing.AdoptedItemIds);
    }

    public async Task DeleteAsync(long userId, long id, CancellationToken ct)
    {
        var visit = await db.Visits.SingleOrDefaultAsync(v => v.Id == id, ct)
            ?? throw ApiException.NotFound("Visit not found");
        if (visit.CreatorId != userId)
        {
            throw ApiException.Forbidden("Not authorized to delete this visit");
        }
        // The database cascades to stops, blocks, quiz, saved_visits and sessions.
        db.Visits.Remove(visit);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureBlockItemsExistAsync(List<BlockRequest> blocks, CancellationToken ct)
    {
        var ids = blocks.Where(b => b.Type == BlockType.Artwork).SelectMany(b => b.Items).Distinct().ToList();
        if (await db.Items.CountAsync(i => ids.Contains(i.Id), ct) != ids.Count)
        {
            throw ApiException.BadRequest("One or more items do not exist");
        }
    }

    private static List<VisitStop> ToStops(List<StopRequest> sequence) =>
        sequence.Select((s, index) => new VisitStop
        {
            Position = index,
            ItemId = s.ItemId!.Value,
            NextDirections = s.NextDirections,
            PrevDirections = s.PrevDirections,
            OverrideImage = s.OverrideImage,
        }).ToList();

    private static List<VisitBlock> ToBlocks(List<BlockRequest> blocks) =>
        blocks.Select((b, index) => new VisitBlock
        {
            Position = index,
            Type = b.Type,
            Name = string.IsNullOrEmpty(b.BlockName) ? "Mainboard" : b.BlockName,
            // A block is either artworks or questions; the other list is dropped.
            Items = b.Type == BlockType.Artwork
                ? b.Items.Select((itemId, i) => new VisitBlockItem { Position = i, ItemId = itemId }).ToList()
                : [],
            Questions = b.Type == BlockType.Questions
                ? b.Questions.Select((q, i) => new VisitBlockQuestion
                {
                    Position = i,
                    Prompt = q.Prompt,
                    AnswerType = q.AnswerType!.Value,
                    Options = q.Options.Select(o => o.Trim()).ToList(),
                    CorrectIndex = q.AnswerType == AnswerType.MultipleChoice ? q.CorrectIndex : null,
                }).ToList()
                : [],
        }).ToList();

    private static List<VisitQuizQuestion> ToQuiz(List<QuizQuestionRequest> quiz) =>
        quiz.Select((q, index) => new VisitQuizQuestion
        {
            Position = index,
            Question = q.Question,
            Options = q.Options,
            CorrectIndex = q.CorrectIndex,
        }).ToList();
}
