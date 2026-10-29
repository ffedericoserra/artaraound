namespace ArtAround.Api.Domain;

public class User
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public Language Language { get; set; } = Language.It;
    public int WalletBalance { get; set; } = 100;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    // Many-to-many through saved_visits (a join table with no payload).
    public List<Visit> SavedVisits { get; set; } = [];
}
