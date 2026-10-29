using ArtAround.Api.Data;
using ArtAround.Api.Domain;
using ArtAround.Api.Dtos;
using ArtAround.Api.Infrastructure;
using ArtAround.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ArtAround.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, IPasswordHasher<User> hasher, TokenService tokens) : ControllerBase
{
    private const string InvalidCredentials = "Invalid credentials";

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        // lower(...) on both sides matches the expression indexes users_*_lower_uq, so
        // "Alice" and "alice" are the same user. The index still decides under a race.
        var taken = await db.Users.AnyAsync(u =>
            u.Username.ToLower() == request.Username.ToLower() || u.Email.ToLower() == request.Email.ToLower(), ct);
        if (taken)
        {
            return ApiProblem.Result(StatusCodes.Status409Conflict, "Username or email already exists");
        }

        var user = new User { Username = request.Username, Email = request.Email };
        user.PasswordHash = hasher.HashPassword(user, request.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new AuthResponse(UserDto.From(user), tokens.CreateToken(user)));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var login = request.Username.ToLower();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username.ToLower() == login || u.Email.ToLower() == login, ct);
        if (user is null)
        {
            // Hash anyway, so "no such user" takes as long as "wrong password" and the
            // response time does not reveal which usernames exist.
            hasher.HashPassword(new User(), request.Password);
            return ApiProblem.Result(StatusCodes.Status401Unauthorized, InvalidCredentials);
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return ApiProblem.Result(StatusCodes.Status401Unauthorized, InvalidCredentials);
        }
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            // The hash was made with older parameters (fewer PBKDF2 iterations): upgrade it.
            user.PasswordHash = hasher.HashPassword(user, request.Password);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new AuthResponse(UserDto.From(user), tokens.CreateToken(user)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

        var myVisits = await ToRefs(db.Visits.Where(v => v.CreatorId == userId).OrderByDescending(v => v.UpdatedAt))
            .ToListAsync(ct);
        var savedVisits = await ToRefs(db.Users.Where(u => u.Id == userId).SelectMany(u => u.SavedVisits).OrderBy(v => v.Title))
            .ToListAsync(ct);

        var u = UserDto.From(user);
        return Ok(new
        {
            user = new ProfileDto(u.Id, u.Username, u.Email, u.AvatarUrl, u.Language, u.WalletBalance,
                u.CreatedAt, u.UpdatedAt, myVisits, savedVisits),
        });
    }

    private static IQueryable<VisitRefDto> ToRefs(IQueryable<Visit> visits) => visits.Select(v => new VisitRefDto(
        v.Id, v.Title, v.Slug, v.Type, v.Length, v.IsPublic, new MuseumRefDto(v.Museum.Id, v.Museum.Name, v.Museum.Slug)));

    [Authorize]
    [HttpPatch("language")]
    public async Task<IActionResult> UpdateLanguage(UpdateLanguageRequest request, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        var user = await db.Users.SingleAsync(u => u.Id == userId, ct);
        user.Language = request.Language!.Value;
        await db.SaveChangesAsync(ct);

        return Ok(new { language = user.Language, user = UserDto.From(user) });
    }

    [Authorize]
    [HttpPatch("wallet")]
    public async Task<IActionResult> RechargeWallet(RechargeWalletRequest request, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        // UPDATE users SET wallet_balance = wallet_balance + @amount WHERE id = @id:
        // the addition happens in the database, so concurrent recharges are never lost.
        await db.Users.Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.WalletBalance, u => u.WalletBalance + request.Amount), ct);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, ct);

        return Ok(new { message = "Wallet recharged successfully", walletBalance = user.WalletBalance, user = UserDto.From(user) });
    }

    /// <summary>Adds the visit to the user's favourites, or removes it if already there.</summary>
    [Authorize]
    [HttpPost("favorites/visits/{visitId:long}")]
    public async Task<IActionResult> ToggleFavoriteVisit(long visitId, CancellationToken ct)
    {
        var userId = User.RequireUserId();
        if (!await db.Visits.AnyAsync(v => v.Id == visitId, ct))
        {
            return ApiProblem.Result(StatusCodes.Status404NotFound, "Visit not found");
        }

        // Interpolated values become SQL parameters, never string concatenation.
        var removed = await db.Database.ExecuteSqlAsync(
            $"DELETE FROM saved_visits WHERE user_id = {userId} AND visit_id = {visitId}", ct);
        if (removed == 0)
        {
            await db.Database.ExecuteSqlAsync(
                $"INSERT INTO saved_visits (user_id, visit_id) VALUES ({userId}, {visitId}) ON CONFLICT DO NOTHING", ct);
        }

        return Ok(new { message = "Preferiti aggiornati", isSaved = removed == 0 });
    }
}
