using Microsoft.AspNetCore.Mvc;

namespace ArtAround.Api.Infrastructure;

/// <summary>
/// The one error shape of the API: RFC 9457 ProblemDetails plus an "error" field.
/// Both frontends display body.error, so it stays; title repeats it for generic clients.
/// </summary>
public static class ApiProblem
{
    public const string ValidationError = "Validation Error";

    public static ProblemDetails Create(int status, string error) => new()
    {
        Status = status,
        Title = error,
        Extensions = { ["error"] = error },
    };

    public static ObjectResult Result(int status, string error) =>
        new(Create(status, error)) { StatusCode = status };

    public static ProblemDetails Validation(IEnumerable<FieldError> details)
    {
        var problem = Create(StatusCodes.Status400BadRequest, ValidationError);
        problem.Extensions["details"] = details.ToList();
        return problem;
    }

    public static ObjectResult ValidationResult(string field, string message) =>
        new(Validation([new FieldError(field, message)])) { StatusCode = StatusCodes.Status400BadRequest };
}

public record FieldError(string Field, string Message);
