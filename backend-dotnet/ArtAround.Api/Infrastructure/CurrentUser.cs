using System.Security.Claims;

namespace ArtAround.Api.Infrastructure;

public static class CurrentUser
{
    /// <summary>The authenticated user's id (the JWT "sub" claim), or null when anonymous.</summary>
    public static long? GetUserId(this ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>For [Authorize] actions, where a user id is guaranteed.</summary>
    public static long RequireUserId(this ClaimsPrincipal principal) =>
        principal.GetUserId() ?? throw new InvalidOperationException("No authenticated user");
}
