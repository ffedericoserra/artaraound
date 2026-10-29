using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ArtAround.Api.Infrastructure;

public static partial class Slug
{
    /// <summary>"MAMbo - Museo d'Arte" → "mambo-museo-darte".</summary>
    public static string From(string text)
    {
        // NFD splits "à" into "a" + a combining accent, which the filter then drops.
        var withoutAccents = new StringBuilder();
        foreach (var c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                withoutAccents.Append(c);
            }
        }

        var cleaned = NotSlugChars().Replace(withoutAccents.ToString(), "").Trim();
        var hyphenated = Separators().Replace(cleaned, "-");
        return hyphenated.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9\s-]")]
    private static partial Regex NotSlugChars();

    [GeneratedRegex(@"[\s-]+")]
    private static partial Regex Separators();
}
