using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ArtAround.Api.Domain;
using ArtAround.Api.Infrastructure;

namespace ArtAround.Api.Dtos;

public record RegisterRequest
{
    [Required(ErrorMessage = "Username is required")]
    [StringLength(30, MinimumLength = 3, ErrorMessage = "Username must be between 3 and 30 characters")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Username { get; init; } = "";

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be between 6 and 100 characters")]
    public string Password { get; init; } = "";
}

public record LoginRequest
{
    /// <summary>A username or an email.</summary>
    [Required(ErrorMessage = "Username or Email is required")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Username { get; init; } = "";

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; init; } = "";
}

public record UpdateLanguageRequest
{
    [Required(ErrorMessage = "Language is required")]
    public Language? Language { get; init; }
}

public record RechargeWalletRequest
{
    [Range(1, 10_000, ErrorMessage = "Recharge amount must be between 1 and 10000")]
    public int Amount { get; init; }
}

public record UserDto(
    [property: JsonPropertyName("_id")] long Id,
    string Username,
    string Email,
    string AvatarUrl,
    Language Language,
    int WalletBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    private const string DefaultAvatar = "/uploads/profiles/default-avatar.jpeg";

    public static UserDto From(User u) => new(
        u.Id, u.Username, u.Email, u.AvatarUrl ?? DefaultAvatar, u.Language, u.WalletBalance, u.CreatedAt, u.UpdatedAt);
}

/// <summary>GET /auth/me: the user plus the visits they made and the ones they saved.</summary>
public record ProfileDto(
    [property: JsonPropertyName("_id")] long Id,
    string Username,
    string Email,
    string AvatarUrl,
    Language Language,
    int WalletBalance,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<VisitRefDto> MyVisits,
    List<VisitRefDto> SavedVisits);

public record VisitRefDto(
    [property: JsonPropertyName("_id")] long Id,
    string Title,
    string Slug,
    VisitType Type,
    VisitLength Length,
    bool IsPublic,
    MuseumRefDto MuseumId);

public record AuthResponse(UserDto User, string Token);
