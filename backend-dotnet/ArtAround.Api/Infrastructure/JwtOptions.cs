namespace ArtAround.Api.Infrastructure;

/// <summary>Bound from the "Jwt" configuration section.</summary>
public class JwtOptions
{
    public const string Section = "Jwt";

    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "artaround";
    public string Audience { get; set; } = "artaround";
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(1);
}
