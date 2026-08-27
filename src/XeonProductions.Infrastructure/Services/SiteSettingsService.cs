using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Typed view over the site_settings key/value table. Everything the theme and the SEO tags
/// need to render lives here, so the admin can change it without a deploy.
/// </summary>
public class SiteSettings
{
    public string SiteTitle { get; set; } = "Xeon Productions";
    public string Tagline { get; set; } = "The future is uncertain, but the end is always near.";

    /// <summary>Absolute base URL, used for canonical tags, feeds and the sitemap.</summary>
    public string SiteUrl { get; set; } = "https://xeonproductions.com";

    public string? LogoUrl { get; set; }

    /// <summary>
    /// Optional alternate logo for the dark theme. A banner drawn for a light background
    /// often disappears against a dark one.
    /// </summary>
    public string? LogoUrlDark { get; set; }

    /// <summary>
    /// Height the logo is drawn at, in pixels. Width follows the image's own proportions,
    /// so a wide banner stays wide instead of being squashed into a square.
    /// </summary>
    public int LogoMaxHeight { get; set; } = 80;

    public HeaderLayout HeaderLayout { get; set; } = HeaderLayout.LogoLeft;

    /// <summary>Show the tagline under the logo or title.</summary>
    public bool ShowTagline { get; set; } = true;

    public string? FaviconUrl { get; set; }

    public int PostsPerPage { get; set; } = 10;
    public string DateFormat { get; set; } = "MMMM d, yyyy";

    /// <summary>
    /// IANA timezone the site publishes in. Permalinks and displayed dates are derived from
    /// this, not from the server clock, so they match what WordPress produced.
    /// </summary>
    public string SiteTimeZone { get; set; } = "America/Chicago";

    public string? FooterText { get; set; }
    public string? ContactEmail { get; set; }

    // --- Theme, mirroring the GeneratePress customiser knobs ---
    public string AccentColor { get; set; } = "#1e73be";

    /// <summary>
    /// Page background behind the content column, in the light theme. The dark theme uses
    /// its own palette.
    /// </summary>
    public string PageBackground { get; set; } = "#f7f8f9";

    /// <summary>
    /// Accent for the dark theme. A blue chosen to sit on white is usually too dim on a
    /// near-black page. Left blank, a brightened version of the main accent is derived.
    /// </summary>
    public string? AccentColorDark { get; set; }
    public int ContentWidth { get; set; } = 1200;
    public int SidebarWidth { get; set; } = 30;
    public ContainerStyle ContainerStyle { get; set; } = ContainerStyle.Separate;
    public SidebarLayout BlogSidebar { get; set; } = SidebarLayout.RightSidebar;
    public SidebarLayout PostSidebar { get; set; } = SidebarLayout.RightSidebar;
    public string BodyFont { get; set; } = "system-ui";
    public string HeadingFont { get; set; } = "system-ui";
    public int BodyFontSize { get; set; } = 17;
    public bool StickyHeader { get; set; } = true;
    public bool ShowThemeToggle { get; set; } = true;

    // --- Content behaviour ---
    public bool EnableComments { get; set; }
    public bool ModerateComments { get; set; } = true;
    public bool ShowAuthorBox { get; set; } = true;
    public bool ShowReadingTime { get; set; } = true;
    /// <summary>
    /// Whether the blog index and archives show whole posts or just a summary. Full content
    /// is the default: this is a weblog, and the front page is where it is read.
    /// </summary>
    public BlogContentDisplay BlogContentDisplay { get; set; } = BlogContentDisplay.FullContent;

    // --- SEO / integrations ---
    public string? SeoTitleSuffix { get; set; }
    public string? DefaultMetaDescription { get; set; }
    public string? DefaultSocialImageUrl { get; set; }
    public string? AnalyticsScript { get; set; }
    public string? GitHubUsername { get; set; }
    public bool SearchEngineVisible { get; set; } = true;
}

public interface ISiteSettingsService
{
    Task<SiteSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SiteSettings settings, CancellationToken ct = default);
    void Invalidate();
}

public class SiteSettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache) : ISiteSettingsService
{
    private const string CacheKey = "site-settings";

    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out SiteSettings? cached) && cached is not null)
            return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.SiteSettings.AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        var settings = new SiteSettings();
        foreach (var prop in typeof(SiteSettings).GetProperties())
        {
            if (!rows.TryGetValue(prop.Name, out var raw) || raw is null) continue;
            try
            {
                var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                object? value = target.IsEnum
                    ? Enum.Parse(target, raw)
                    : Convert.ChangeType(raw, target);
                prop.SetValue(settings, value);
            }
            catch
            {
                // A malformed row must never take the whole site down; keep the default.
            }
        }

        // Publishing dates and permalinks read this statically, so keep it in step.
        SiteTime.Configure(settings.SiteTimeZone);

        cache.Set(CacheKey, settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    public async Task SaveAsync(SiteSettings settings, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.SiteSettings.ToDictionaryAsync(x => x.Key, ct);

        foreach (var prop in typeof(SiteSettings).GetProperties())
        {
            var value = prop.GetValue(settings)?.ToString();

            if (existing.TryGetValue(prop.Name, out var row))
            {
                if (row.Value == value) continue;
                row.Value = value;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.SiteSettings.Add(new SiteSetting { Key = prop.Name, Value = value });
            }
        }

        await db.SaveChangesAsync(ct);
        Invalidate();
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
