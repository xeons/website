using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Endpoints;

public static class FeedEndpoints
{
    public static IEndpointRouteBuilder MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/feed.xml", BuildFeed);

        // WordPress served the feed from /feed; keep that URL alive for existing readers.
        app.MapGet("/feed", (HttpContext ctx) =>
        {
            ctx.Response.Redirect("/feed.xml", permanent: true);
            return Task.CompletedTask;
        });

        return app;
    }

    private static async Task BuildFeed(
        HttpContext http,
        AppDbContext db,
        ISiteSettingsService settingsService,
        IHtmlService html,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var baseUrl = settings.SiteUrl.TrimEnd('/');

        var posts = await db.Posts.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && p.PublishedAt <= DateTimeOffset.UtcNow)
            .OrderByDescending(p => p.PublishedAt)
            .Take(25)
            .Include(p => p.Categories)
            .ToListAsync(ct);

        XNamespace content = "http://purl.org/rss/1.0/modules/content/";
        XNamespace atom = "http://www.w3.org/2005/Atom";

        var channel = new XElement("channel",
            new XElement("title", settings.SiteTitle),
            new XElement("link", baseUrl),
            new XElement("description", settings.Tagline),
            new XElement("language", "en-US"),
            new XElement("lastBuildDate", DateTimeOffset.UtcNow.ToString("r")),
            new XElement("generator", "Xeon Productions CMS"),
            new XElement(atom + "link",
                new XAttribute("href", $"{baseUrl}/feed.xml"),
                new XAttribute("rel", "self"),
                new XAttribute("type", "application/rss+xml")));

        foreach (var post in posts)
        {
            var url = $"{baseUrl}{PermalinkFor(post.PublishedAt, post.Slug)}";
            var summary = post.Excerpt ?? html.BuildExcerpt(post.ContentHtml, 300);

            var item = new XElement("item",
                new XElement("title", post.Title),
                new XElement("link", url),
                // A stable, never-reused id; the URL doubles as one here.
                new XElement("guid", new XAttribute("isPermaLink", "true"), url),
                new XElement("pubDate", (post.PublishedAt ?? post.CreatedAt).ToString("r")),
                new XElement("description", summary),
                new XElement(content + "encoded", new XCData(post.ContentHtml)));

            foreach (var category in post.Categories)
                item.Add(new XElement("category", category.Name));

            channel.Add(item);
        }

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("rss",
                new XAttribute("version", "2.0"),
                new XAttribute(XNamespace.Xmlns + "content", content),
                new XAttribute(XNamespace.Xmlns + "atom", atom),
                channel));

        http.Response.ContentType = "application/rss+xml; charset=utf-8";
        http.Response.Headers.CacheControl = "public,max-age=1800";
        await http.Response.WriteAsync(doc.ToString(), Encoding.UTF8, ct);
    }

    /// <summary>
    /// The WordPress permalink shape, kept so every existing inbound link still resolves.
    ///
    /// The date parts come from the site's timezone, not the server's. WordPress used the
    /// site clock, so a post published late in the evening in Chicago belongs to that day
    /// even though the same instant is already tomorrow in UTC.
    /// </summary>
    public static string PermalinkFor(DateTimeOffset? publishedAt, string slug)
    {
        var date = SiteTime.ToSiteTime(publishedAt ?? DateTimeOffset.UtcNow);
        return $"/{date:yyyy}/{date:MM}/{date:dd}/{slug}";
    }
}
