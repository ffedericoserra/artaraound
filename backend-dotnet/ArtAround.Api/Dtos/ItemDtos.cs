using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ArtAround.Api.Domain;
using ArtAround.Api.Infrastructure;

namespace ArtAround.Api.Dtos;

public record UserRefDto([property: JsonPropertyName("_id")] long Id, string Username);

public record DescriptionDto(Tone Tone, string Text);

public record ItemDto(
    [property: JsonPropertyName("_id")] long Id,
    string ContentId,
    UserRefDto CreatorId,
    string TargetAudience,
    List<DescriptionDto> Descriptions,
    int Price,
    License License,
    bool IsPublic,
    int SalesCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Revenue,
    List<ContentDto> AssociatedContents,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsOwned,
    bool IsPurchased,
    int AdoptionPrice);

/// <summary>An item as embedded in visit lists: just enough to resolve the stop's content.</summary>
public record ItemRefDto([property: JsonPropertyName("_id")] long Id, string ContentId);

public record PurchaseResult(int ChargedAmount, int WalletBalance, List<long> AdoptedItemIds);

public record PurchaseResponse(string Message, int ChargedAmount, int WalletBalance, List<long> AdoptedItemIds);

public record DescriptionRequest
{
    [Required(ErrorMessage = "Tone is required")]
    public Tone? Tone { get; init; }

    [Required(ErrorMessage = "Description text is required")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string Text { get; init; } = "";
}

public static class TargetAudiences
{
    public static readonly string[] All = ["general", "children", "student", "expert", "tourist"];
}

public record CreateItemRequest : IValidatableObject
{
    /// <summary>The content's universalId, not its numeric id.</summary>
    [Required(ErrorMessage = "Content ID is required")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string ContentId { get; init; } = "";

    [Required(ErrorMessage = "Target audience is required")]
    [AllowedValues("general", "children", "student", "expert", "tourist",
        ErrorMessage = "Target audience must be one of: general, children, student, expert, tourist")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string TargetAudience { get; init; } = "";

    [Required]
    [MinLength(1, ErrorMessage = "At least one description is required")]
    public List<DescriptionRequest> Descriptions { get; init; } = [];

    [Range(0, int.MaxValue, ErrorMessage = "Price must be 0 or more")]
    public int Price { get; init; }

    public License License { get; init; } = License.CcBy;
    public bool IsPublic { get; init; }
    public List<long> AssociatedContents { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext context) =>
        ItemValidation.DistinctTones(Descriptions);
}

/// <summary>PUT /items/:id. A null property means "leave unchanged"; contentId cannot change.</summary>
public record UpdateItemRequest : IValidatableObject
{
    [AllowedValues(null, "general", "children", "student", "expert", "tourist",
        ErrorMessage = "Target audience must be one of: general, children, student, expert, tourist")]
    [JsonConverter(typeof(TrimmedStringConverter))]
    public string? TargetAudience { get; init; }

    [MinLength(1, ErrorMessage = "At least one description is required")]
    public List<DescriptionRequest>? Descriptions { get; init; }

    [Range(0, int.MaxValue, ErrorMessage = "Price must be 0 or more")]
    public int? Price { get; init; }

    public License? License { get; init; }
    public bool? IsPublic { get; init; }
    public List<long>? AssociatedContents { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context) =>
        ItemValidation.DistinctTones(Descriptions ?? []);
}

internal static class ItemValidation
{
    // item_texts has PK (item_id, tone): a second text for a tone would be a duplicate key.
    public static IEnumerable<ValidationResult> DistinctTones(List<DescriptionRequest> descriptions)
    {
        if (descriptions.Select(d => d.Tone).Distinct().Count() != descriptions.Count)
        {
            yield return new ValidationResult("Each tone can appear only once", ["descriptions"]);
        }
    }
}
