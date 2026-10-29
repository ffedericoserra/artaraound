namespace ArtAround.Api.Infrastructure;

/// <summary>
/// An expected failure with a status code and a message for the client, thrown from
/// services that have no access to IActionResult. ApiExceptionHandler turns it into
/// ProblemDetails. Controllers return their own errors directly instead.
/// </summary>
public class ApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static ApiException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static ApiException Forbidden(string message) => new(StatusCodes.Status403Forbidden, message);
    public static ApiException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
}
