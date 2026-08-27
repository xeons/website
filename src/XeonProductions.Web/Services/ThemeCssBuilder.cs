using System.Globalization;
using System.Text;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Services;

/// <summary>
/// Turns the admin's theme settings into the handful of CSS custom properties the stylesheet
/// reads. Everything else lives in static CSS, so this block stays small.
/// </summary>
public class ThemeCssBuilder(ISiteSettingsService settingsService)
{
    public async Task<string> BuildAsync(CancellationToken ct = default)
    {
        var s = await settingsService.GetAsync(ct);

        var accent = Sanitize(s.AccentColor, "#1e73be");
        var pageBackground = Sanitize(s.PageBackground, "#f7f8f9");

        // Falls back to a brightened main accent, since the same blue rarely reads well
        // on both a white and a near-black page.
        var accentDark = string.IsNullOrWhiteSpace(s.AccentColorDark)
            ? Shade(accent, 0.30)
            : Sanitize(s.AccentColorDark, Shade(accent, 0.30));
        var (r, g, b) = ParseRgb(accent);

        var sb = new StringBuilder();
        sb.Append(":root{");
        sb.Append(CultureInfo.InvariantCulture, $"--accent:{accent};");
        sb.Append(CultureInfo.InvariantCulture, $"--accent-rgb:{r},{g},{b};");
        sb.Append(CultureInfo.InvariantCulture, $"--accent-hover:{Shade(accent, -0.18)};");
        sb.Append(CultureInfo.InvariantCulture, $"--content-width:{Math.Clamp(s.ContentWidth, 640, 1600)}px;");
        sb.Append(CultureInfo.InvariantCulture, $"--sidebar-width:{Math.Clamp(s.SidebarWidth, 20, 40)}%;");
        sb.Append(CultureInfo.InvariantCulture, $"--font-body:{FontStack(s.BodyFont)};");
        sb.Append(CultureInfo.InvariantCulture, $"--font-heading:{FontStack(s.HeadingFont)};");
        sb.Append(CultureInfo.InvariantCulture, $"--font-size-body:{Math.Clamp(s.BodyFontSize, 13, 24)}px;");
        sb.Append(CultureInfo.InvariantCulture, $"--logo-max-height:{Math.Clamp(s.LogoMaxHeight, 24, 320)}px;");
        sb.Append(CultureInfo.InvariantCulture, $"--page-bg:{pageBackground};");

        // Some text sits directly on the page background rather than inside a card, so its
        // colour has to follow whatever background was chosen. A dark page with the default
        // dark body colour would render archive headings invisible.
        var onDark = IsDark(pageBackground);
        sb.Append(CultureInfo.InvariantCulture, $"--text-on-bg:{(onDark ? "#e8eaed" : "#222831")};");
        sb.Append(CultureInfo.InvariantCulture, $"--text-muted-on-bg:{(onDark ? "#9aa4b2" : "#6b7280")};");
        sb.Append(CultureInfo.InvariantCulture, $"--border-on-bg:{(onDark ? "rgba(255,255,255,.14)" : "#e3e6e9")};");

        var (dr, dg, dbl) = ParseRgb(accentDark);
        sb.Append(CultureInfo.InvariantCulture, $"--accent-dark:{accentDark};");
        sb.Append(CultureInfo.InvariantCulture, $"--accent-dark-rgb:{dr},{dg},{dbl};");
        // Hover lightens on a dark page, where the light theme darkens.
        sb.Append(CultureInfo.InvariantCulture, $"--accent-dark-hover:{Shade(accentDark, 0.22)};");
        sb.Append('}');

        return sb.ToString();
    }

    /// <summary>
    /// The value is interpolated into a style block, so anything that is not a plain hex
    /// colour is discarded rather than escaped.
    /// </summary>
    private static string Sanitize(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;

        var candidate = value.Trim();
        if (candidate.Length is not (4 or 7) || candidate[0] != '#') return fallback;

        return candidate[1..].All(Uri.IsHexDigit) ? candidate : fallback;
    }

    private static string FontStack(string? family)
    {
        var systemStack =
            "system-ui,-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif";

        if (string.IsNullOrWhiteSpace(family) ||
            family.Equals("system-ui", StringComparison.OrdinalIgnoreCase))
        {
            return systemStack;
        }

        // Quotes and semicolons would break out of the declaration.
        var clean = new string(family.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-').ToArray()).Trim();
        return string.IsNullOrEmpty(clean) ? systemStack : $"'{clean}',{systemStack}";
    }

    /// <summary>
    /// Perceived brightness, using the standard luma weighting rather than a plain average,
    /// because the eye is far more sensitive to green than to blue.
    /// </summary>
    private static bool IsDark(string hex)
    {
        var (r, g, b) = ParseRgb(hex);
        return (0.299 * r + 0.587 * g + 0.114 * b) < 140;
    }

    private static (int R, int G, int B) ParseRgb(string hex)
    {
        var body = hex[1..];
        if (body.Length == 3)
            body = string.Concat(body.Select(c => $"{c}{c}"));

        return (
            Convert.ToInt32(body[..2], 16),
            Convert.ToInt32(body[2..4], 16),
            Convert.ToInt32(body[4..6], 16));
    }

    /// <summary>Lightens or darkens a hex colour; negative amounts darken.</summary>
    private static string Shade(string hex, double amount)
    {
        var (r, g, b) = ParseRgb(hex);

        int Adjust(int channel) => amount < 0
            ? (int)Math.Round(channel * (1 + amount))
            : (int)Math.Round(channel + (255 - channel) * amount);

        return $"#{Adjust(r):x2}{Adjust(g):x2}{Adjust(b):x2}";
    }
}
