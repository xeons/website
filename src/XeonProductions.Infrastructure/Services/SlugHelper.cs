using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XeonProductions.Infrastructure.Services;

public static partial class SlugHelper
{
    /// <summary>
    /// Turns a title into a URL segment: lowercase, ASCII, hyphen separated.
    /// </summary>
    public static string Slugify(string? input, int maxLength = 180)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Strip diacritics so "Creme" becomes "creme" rather than being dropped.
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var slug = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();

        // C# and C++ would otherwise collapse to the same slug as C.
        slug = slug.Replace("c#", "c-sharp").Replace("c++", "c-plus-plus").Replace(".net", "dotnet");

        slug = Invalid().Replace(slug, "-");
        slug = Dashes().Replace(slug, "-").Trim('-');

        if (slug.Length > maxLength)
        {
            slug = slug[..maxLength];
            var lastDash = slug.LastIndexOf('-');
            if (lastDash > maxLength / 2) slug = slug[..lastDash];
        }

        return slug;
    }

    /// <summary>
    /// Appends -2, -3 ... until the slug clears the caller's uniqueness check.
    /// </summary>
    public static async Task<string> MakeUniqueAsync(
        string baseSlug,
        Func<string, Task<bool>> exists)
    {
        var slug = string.IsNullOrEmpty(baseSlug) ? "untitled" : baseSlug;
        if (!await exists(slug)) return slug;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{slug}-{i}";
            if (!await exists(candidate)) return candidate;
        }

        return $"{slug}-{Guid.NewGuid():N}"[..180];
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex Invalid();

    [GeneratedRegex("-{2,}")]
    private static partial Regex Dashes();
}
