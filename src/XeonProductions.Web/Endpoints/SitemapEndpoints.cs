using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Endpoints;

public static class SitemapEndpoints
{
    public static IEndpointRouteBuilder MapSitemapEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", BuildSitemap);
        app.MapGet("/robots.txt", BuildRobots);
        return app;
    }

    private static async Task BuildSitemap(
        HttpContext http,
        AppDbContext db,
        IContentService content,
        ISiteSettingsService settingsService,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var baseUrl = settings.SiteUrl.TrimEnd('/');

        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var root = new XElement(ns + "urlset");

        void Add(string path, DateTimeOffset? modified, string changeFreq, string priority) =>
            root.Add(new XElement(ns + "url",
                new XElement(ns + "loc", $"{baseUrl}{path}"),
                modified is null
                    ? null
                    : new XElement(ns + "lastmod", modified.Value.ToString("yyyy-MM-dd")),
                new XElement(ns + "changefreq", changeFreq),
                new XElement(ns + "priority", priority)));

        Add("/", DateTimeOffset.UtcNow, "daily", "1.0");

        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published
                     && p.PublishedAt <= DateTimeOffset.UtcNow
                     && !p.NoIndex)
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new { p.Slug, p.PublishedAt, p.UpdatedAt })
            .ToListAsync(ct);

        foreach (var post in posts)
            Add(FeedEndpoints.PermalinkFor(post.PublishedAt, post.Slug), post.UpdatedAt, "monthly", "0.8");

        var pages = await db.Pages.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && !p.NoIndex)
            .Select(p => new { p.Id, p.UpdatedAt })
            .ToListAsync(ct);

        foreach (var page in pages)
        {
            var path = await content.GetPagePathAsync(page.Id, ct);
            Add(path, page.UpdatedAt, "monthly", "0.6");
        }

        var categories = await db.Categories.AsNoTracking()
            // An empty archive is a thin page; leave it out of the index.
            .Where(c => c.Posts.Any(p => p.Status == ContentStatus.Published))
            .Select(c => c.Slug)
            .ToListAsync(ct);

        foreach (var slug in categories)
            Add($"/category/{slug}", null, "weekly", "0.5");

        var tags = await db.Tags.AsNoTracking()
            .Where(t => t.Posts.Any(p => p.Status == ContentStatus.Published))
            .Select(t => t.Slug)
            .ToListAsync(ct);

        foreach (var slug in tags)
            Add($"/tag/{slug}", null, "monthly", "0.3");

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);

        http.Response.ContentType = "application/xml; charset=utf-8";
        http.Response.Headers.CacheControl = "public,max-age=3600";
        await http.Response.WriteAsync(doc.ToString(), Encoding.UTF8, ct);
    }

    private static async Task BuildRobots(
        HttpContext http, ISiteSettingsService settingsService, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var baseUrl = settings.SiteUrl.TrimEnd('/');

        var sb = new StringBuilder();
        sb.AppendLine("User-agent: *");

        if (settings.SearchEngineVisible)
        {
            sb.AppendLine("Disallow: /admin");
            sb.AppendLine("Disallow: /search");

            sb.AppendLine("Disallow: /download/");
            sb.AppendLine();
            sb.AppendLine($"Sitemap: {baseUrl}/sitemap.xml");
        }
        else
        {
            // Mirrors the "discourage search engines" switch in the admin settings.
            sb.AppendLine("Disallow: /");
        }

        http.Response.ContentType = "text/plain; charset=utf-8";
        await http.Response.WriteAsync(sb.ToString(), Encoding.UTF8, ct);
    }
}
