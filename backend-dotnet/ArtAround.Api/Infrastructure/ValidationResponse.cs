using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ArtAround.Api.Infrastructure;

public static class ValidationResponse
{
    /// <summary>
    /// Replaces [ApiController]'s automatic 400 body with the API's shape:
    /// details[] of { field, message }, one per error, in the order they were found.
    /// </summary>
    public static IActionResult FromModelState(ActionContext context)
    {
        var details = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .SelectMany(entry => entry.Value!.Errors.Select(error =>
                new FieldError(FieldName(entry.Key), Message(error))));

        return new BadRequestObjectResult(ApiProblem.Validation(details));
    }

    // JSON errors are keyed by path ("$.price"), DataAnnotations errors by name ("price").
    private static string FieldName(string key) => key.StartsWith("$.") ? key[2..] : key.TrimStart('$');

    private static string Message(ModelError error) =>
        string.IsNullOrEmpty(error.ErrorMessage) ? "Invalid value" : error.ErrorMessage;
}
