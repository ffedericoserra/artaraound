using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ArtAround.Api.Infrastructure;

/// <summary>
/// Last line of defence: maps exceptions that escape a controller to ProblemDetails.
/// Constraint violations become 409 because the database, not the app, had the final
/// word (two requests passed the app's check at once and one lost the race).
/// </summary>
public class ApiExceptionHandler(IProblemDetailsService problemDetails, ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, error) = exception switch
        {
            ApiException api => (api.StatusCode, api.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict,
                "The resource was modified by another request; reload and retry"),
            DbUpdateException { InnerException: PostgresException pg } => FromPostgres(pg),
            PostgresException pg => FromPostgres(pg),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
        };

        if (status >= 500)
        {
            logger.LogError(exception, "Unhandled exception");
        }

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = ApiProblem.Create(status, error),
            Exception = exception,
        });
    }

    private static (int, string) FromPostgres(PostgresException pg) => pg.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => (StatusCodes.Status409Conflict, "Duplicate Error"),
        PostgresErrorCodes.ForeignKeyViolation => (StatusCodes.Status409Conflict,
            "The resource is referenced by other data"),
        PostgresErrorCodes.CheckViolation => (StatusCodes.Status400BadRequest, "Constraint violated"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
    };
}
