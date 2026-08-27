using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// One-way import from the WordPress REST API into this schema. It reads the public
/// endpoints only, so it needs no credentials on the WordPress side.
/// </summary>
public partial class WordPressImporter(
    AppDbContext db,
    IMediaService media,
    IHtmlService html,
    IWordPressMarkupCleaner cleaner,
    IImportLinkRewriter linkRewriter,
    HttpClient http,
    ILogger<WordPressImporter> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // WordPress ids map to ours as we go, so relationships survive the move.
    private readonly Dictionary<int, int> _categoryMap = [];
    private readonly Dictionary<int, int> _tagMap = [];
    private readonly Dictionary<int, int> _mediaMap = [];
    private readonly Dictionary<int, int> _pageMap = [];
    private readonly Dictionary<int, int> _postMap = [];
    private readonly Dictionary<string, string> _urlRewrites = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImportReport> RunAsync(ImportOptions options, CancellationToken ct = default)
    {
        var report = new ImportReport();
        var baseUrl = options.SourceUrl.TrimEnd('/');

        var authorId = options.AuthorId
            ?? await db.Users.Select(u => u.Id).FirstOrDefaultAsync(ct);

        logger.LogInformation("Importing from {Source}{DryRun}",
            baseUrl, options.DryRun ? " (dry run)" : string.Empty);

        await ImportCategoriesAsync(baseUrl, options, report, ct);
        await ImportTagsAsync(baseUrl, options, report, ct);

        if (options.ImportMedia)
        {
            await ImportMediaAsync(baseUrl, options, report, authorId, ct);
        }

        // Pages first, in two passes, so a child can find its parent.
        await ImportPagesAsync(baseUrl, options, report, authorId, ct);
        await ImportPostsAsync(baseUrl, options, report, authorId, ct);

        // Last, once every page and post exists and can be pointed at.
        if (!options.DryRun)
        {
            await RewriteInternalLinksAsync(baseUrl, report, ct);
        }

        logger.LogInformation("Import finished: {Report}", report);
        return report;
    }

    // --- Taxonomy ---

    private async Task ImportCategoriesAsync(
        string baseUrl, ImportOptions options, ImportReport report, CancellationToken ct)
    {
        var items = await FetchAllAsync<WpTerm>($"{baseUrl}/wp-json/wp/v2/categories", ct);

        // Two passes: create every term, then wire up the parents.
        foreach (var item in items)
        {
            var existing = await db.Categories.FirstOrDefaultAsync(c => c.Slug == item.Slug, ct);

            if (existing is not null)
            {
                _categoryMap[item.Id] = existing.Id;
                continue;
            }

            var category = new Category
            {
                Name = WebUtilityDecode(item.Name) ?? item.Slug,
                Slug = item.Slug,
                Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description
            };

            if (options.DryRun)
            {
                report.Categories++;
                continue;
            }

            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);

            _categoryMap[item.Id] = category.Id;
            report.Categories++;
        }

        if (options.DryRun) return;

        foreach (var item in items.Where(i => i.Parent > 0))
        {
            if (!_categoryMap.TryGetValue(item.Id, out var localId)) continue;
            if (!_categoryMap.TryGetValue(item.Parent, out var localParentId)) continue;

            var category = await db.Categories.FirstAsync(c => c.Id == localId, ct);
            category.ParentId = localParentId;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ImportTagsAsync(
        string baseUrl, ImportOptions options, ImportReport report, CancellationToken ct)
    {
        var items = await FetchAllAsync<WpTerm>($"{baseUrl}/wp-json/wp/v2/tags", ct);

        foreach (var item in items)
        {
            var existing = await db.Tags.FirstOrDefaultAsync(t => t.Slug == item.Slug, ct);

            if (existing is not null)
            {
                _tagMap[item.Id] = existing.Id;
                continue;
            }

            var tag = new Tag { Name = WebUtilityDecode(item.Name) ?? item.Slug, Slug = item.Slug };

            if (options.DryRun)
            {
                report.Tags++;
                continue;
            }

            db.Tags.Add(tag);
            await db.SaveChangesAsync(ct);

            _tagMap[item.Id] = tag.Id;
            report.Tags++;
        }
    }

    // --- Media ---

    private async Task ImportMediaAsync(
        string baseUrl, ImportOptions options, ImportReport report,
        string? authorId, CancellationToken ct)
    {
        var items = await FetchAllAsync<WpMedia>($"{baseUrl}/wp-json/wp/v2/media", ct);

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.SourceUrl)) continue;

            var existing = await db.Media.FirstOrDefaultAsync(m => m.SourceUrl == item.SourceUrl, ct);

            if (existing is not null)
            {
                _mediaMap[item.Id] = existing.Id;
                _urlRewrites[item.SourceUrl] = media.PublicUrl(existing.RelativePath);
                report.Skipped++;
                continue;
            }

            if (options.DryRun)
            {
                report.Media++;
                continue;
            }

            try
            {
                using var response = await http.GetAsync(item.SourceUrl, ct);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(ct);

                var fileName = Path.GetFileName(new Uri(item.SourceUrl).AbsolutePath);
                var contentType = response.Content.Headers.ContentType?.MediaType
                    ?? item.MimeType
                    ?? "application/octet-stream";

                var result = await media.SaveAsync(
                    stream, fileName, contentType, authorId, item.SourceUrl, ct);

                if (!result.Success || result.Item is null)
                {
                    report.MediaFailed++;
                    report.Warnings.Add($"Media {item.SourceUrl}: {result.Error}");
                    continue;
                }

                // The media service saved this through its own context, so the entity is not
                // tracked here. Update by id rather than by mutating a detached instance.
                var altText = string.IsNullOrWhiteSpace(item.AltText) ? null : item.AltText;
                var title = WebUtilityDecode(item.Title?.Rendered);
                var caption = StripTags(item.Caption?.Rendered);

                await db.Media
                    .Where(m => m.Id == result.Item.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.AltText, altText)
                        .SetProperty(m => m.Title, title)
                        .SetProperty(m => m.Caption, caption), ct);

                _mediaMap[item.Id] = result.Item.Id;
                _urlRewrites[item.SourceUrl] = media.PublicUrl(result.Item.RelativePath);
                report.Media++;
            }
            catch (Exception ex)
            {
                report.MediaFailed++;
                report.Warnings.Add($"Media {item.SourceUrl}: {ex.Message}");
                logger.LogWarning(ex, "Could not import {Url}", item.SourceUrl);
            }
        }
    }

    // --- Pages ---

    private async Task ImportPagesAsync(
        string baseUrl, ImportOptions options, ImportReport report,
        string? authorId, CancellationToken ct)
    {
        var items = await FetchAllAsync<WpPost>($"{baseUrl}/wp-json/wp/v2/pages?status=publish", ct);

        // Shallowest first, so a parent exists before its children are created.
        foreach (var item in items.OrderBy(i => Depth(items, i)))
        {
            var slug = item.Slug;
            var parentId = item.Parent > 0 ? _pageMap.GetValueOrDefault(item.Parent) : (int?)null;
            if (parentId == 0) parentId = null;

            var existing = await db.Pages
                .FirstOrDefaultAsync(p => p.Slug == slug && p.ParentId == parentId, ct);

            if (existing is not null && !options.Overwrite)
            {
                _pageMap[item.Id] = existing.Id;
                report.Skipped++;
                continue;
            }

            if (options.DryRun)
            {
                report.Pages++;
                continue;
            }

            var page = existing ?? new Page { Slug = slug, CreatedAt = ParseDate(item.DateGmt) };

            page.Title = WebUtilityDecode(item.Title?.Rendered) ?? slug;
            page.ContentHtml = html.Sanitize(cleaner.Clean(RewriteUrls(item.Content?.Rendered)));
            page.Status = ContentStatus.Published;
            page.ParentId = parentId;
            page.MenuOrder = item.MenuOrder;
            page.AuthorId = authorId;
            page.PublishedAt = ParseDate(item.DateGmt);
            page.UpdatedAt = ParseDate(item.ModifiedGmt);
            page.FeaturedImageId = ResolveFeaturedImage(item.FeaturedMedia);

            if (existing is null) db.Pages.Add(page);

            await db.SaveChangesAsync(ct);

            _pageMap[item.Id] = page.Id;
            report.Pages++;
        }
    }

    private static int Depth(List<WpPost> all, WpPost item)
    {
        var depth = 0;
        var cursor = item;

        while (cursor.Parent > 0 && depth < 20)
        {
            var parent = all.FirstOrDefault(p => p.Id == cursor.Parent);
            if (parent is null) break;

            cursor = parent;
            depth++;
        }

        return depth;
    }

    // --- Posts ---

    private async Task ImportPostsAsync(
        string baseUrl, ImportOptions options, ImportReport report,
        string? authorId, CancellationToken ct)
    {
        var items = await FetchAllAsync<WpPost>($"{baseUrl}/wp-json/wp/v2/posts?status=publish", ct);

        foreach (var item in items)
        {
            var existing = await db.Posts
                .Include(p => p.Categories)
                .Include(p => p.Tags)
                .FirstOrDefaultAsync(p => p.Slug == item.Slug, ct);

            if (existing is not null && !options.Overwrite)
            {
                report.Skipped++;
                continue;
            }

            if (options.DryRun)
            {
                report.Posts++;
                continue;
            }

            var post = existing ?? new Post { Slug = item.Slug, CreatedAt = ParseDate(item.DateGmt) };

            post.Title = WebUtilityDecode(item.Title?.Rendered) ?? item.Slug;
            post.ContentHtml = html.Sanitize(cleaner.Clean(RewriteUrls(item.Content?.Rendered)));

            var excerpt = CleanExcerpt(item.Excerpt?.Rendered);
            post.Excerpt = string.IsNullOrWhiteSpace(excerpt)
                ? html.BuildExcerpt(post.ContentHtml)
                : excerpt;

            post.Status = ContentStatus.Published;
            post.AuthorId = authorId;
            post.PublishedAt = ParseDate(item.DateGmt);
            post.UpdatedAt = ParseDate(item.ModifiedGmt);
            post.IsSticky = item.Sticky;
            post.FeaturedImageId = ResolveFeaturedImage(item.FeaturedMedia);

            post.Categories.Clear();
            foreach (var wpId in item.Categories ?? [])
            {
                if (!_categoryMap.TryGetValue(wpId, out var localId)) continue;

                var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == localId, ct);
                if (category is not null) post.Categories.Add(category);
            }

            post.Tags.Clear();
            foreach (var wpId in item.Tags ?? [])
            {
                if (!_tagMap.TryGetValue(wpId, out var localId)) continue;

                var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == localId, ct);
                if (tag is not null) post.Tags.Add(tag);
            }

            if (existing is null) db.Posts.Add(post);

            await db.SaveChangesAsync(ct);

            _postMap[item.Id] = post.Id;
            report.Posts++;
        }
    }

    private int? ResolveFeaturedImage(int wpMediaId) =>
        wpMediaId > 0 && _mediaMap.TryGetValue(wpMediaId, out var localId) ? localId : null;

    // --- Internal links ---

    /// <summary>
    /// Repoints imported content at this site. It runs as a final pass because a link can
    /// name any other page or post, and those ids are only all known once everything is in.
    /// </summary>
    private async Task RewriteInternalLinksAsync(string baseUrl, ImportReport report, CancellationToken ct)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var sourceUri))
        {
            hosts.Add(sourceUri.Host);
            hosts.Add(sourceUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? sourceUri.Host[4..]
                : "www." + sourceUri.Host);
        }

        var pagePaths = new Dictionary<int, string>();
        var localPages = await db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.Slug, p.ParentId })
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var (wpId, localId) in _pageMap)
        {
            var segments = new List<string>();
            var cursor = (int?)localId;
            var guard = 0;

            while (cursor is int id && localPages.TryGetValue(id, out var node) && guard++ < 20)
            {
                segments.Insert(0, node.Slug);
                cursor = node.ParentId;
            }

            if (segments.Count > 0) pagePaths[wpId] = "/" + string.Join('/', segments);
        }

        var permalinks = new Dictionary<int, string>();
        var localPosts = await db.Posts.AsNoTracking()
            .Select(p => new { p.Id, p.Slug, p.PublishedAt })
            .ToDictionaryAsync(p => p.Id, ct);

        foreach (var (wpId, localId) in _postMap)
        {
            if (!localPosts.TryGetValue(localId, out var post)) continue;

            var date = SiteTime.ToSiteTime(post.PublishedAt ?? DateTimeOffset.UtcNow);
            permalinks[wpId] = $"/{date:yyyy}/{date:MM}/{date:dd}/{post.Slug}";
        }

        var targets = new LinkTargets
        {
            SourceHosts = hosts,
            PagePaths = pagePaths,
            PostPermalinks = permalinks,
            MediaUrls = _urlRewrites
        };

        var rewritten = 0;

        foreach (var localId in _pageMap.Values.Distinct())
        {
            var page = await db.Pages.FirstOrDefaultAsync(p => p.Id == localId, ct);
            if (page is null) continue;

            var updated = linkRewriter.Rewrite(page.ContentHtml, targets);
            if (updated == page.ContentHtml) continue;

            page.ContentHtml = updated;
            rewritten++;
        }

        foreach (var localId in _postMap.Values.Distinct())
        {
            var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == localId, ct);
            if (post is null) continue;

            var updated = linkRewriter.Rewrite(post.ContentHtml, targets);
            if (updated == post.ContentHtml) continue;

            post.ContentHtml = updated;
            rewritten++;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Repointed internal links in {Count} entries.", rewritten);
    }

    // --- Helpers ---

    /// <summary>
    /// Points in-content attachment URLs at the local media store. Anything not imported is
    /// left pointing at the original host rather than being broken.
    /// </summary>
    private string? RewriteUrls(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return content;

        foreach (var (from, to) in _urlRewrites)
        {
            content = content.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }

        return content;
    }

    private async Task<List<T>> FetchAllAsync<T>(string url, CancellationToken ct)
    {
        var results = new List<T>();
        var separator = url.Contains('?') ? "&" : "?";

        for (var page = 1; page <= 100; page++)
        {
            var pageUrl = $"{url}{separator}per_page=100&page={page}";

            using var response = await http.GetAsync(pageUrl, ct);

            // WordPress answers 400 once the page number runs past the last page.
            if (!response.IsSuccessStatusCode) break;

            var batch = await response.Content.ReadFromJsonAsync<List<T>>(Json, ct);
            if (batch is null || batch.Count == 0) break;

            results.AddRange(batch);
            if (batch.Count < 100) break;
        }

        return results;
    }

    private static DateTimeOffset ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, out var parsed)
            // The GMT fields carry no offset, so state it explicitly.
            ? new DateTimeOffset(parsed.DateTime, TimeSpan.Zero)
            : DateTimeOffset.UtcNow;

    private static string? WebUtilityDecode(string? value) =>
        value is null ? null : System.Net.WebUtility.HtmlDecode(value).Trim();

    /// <summary>
    /// Returns a hand-written excerpt, or null when WordPress generated one for us.
    ///
    /// The theme appends a "Read more" anchor to every auto-generated excerpt. Stripping the
    /// tags would leave the words "... Read more" sitting in the text, right next to the
    /// listing's own "Continue reading" link. An auto-excerpt carries no information the
    /// content does not, so it is discarded and rebuilt from the post body instead.
    /// </summary>
    private static string? CleanExcerpt(string? rendered)
    {
        if (string.IsNullOrWhiteSpace(rendered)) return null;
        if (AutoExcerptLink().IsMatch(rendered)) return null;

        return StripTags(rendered);
    }

    [GeneratedRegex("""class\s*=\s*["'][^"']*\b(read-more|more-link)\b""", RegexOptions.IgnoreCase)]
    private static partial Regex AutoExcerptLink();

    private static string? StripTags(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = TagPattern().Replace(value, string.Empty);
        return System.Net.WebUtility.HtmlDecode(text).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagPattern();

    // --- WordPress REST shapes, only the fields we consume ---

    private sealed class WpTerm
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int Parent { get; set; }
    }

    private sealed class WpRendered
    {
        public string? Rendered { get; set; }
    }

    private sealed class WpMedia
    {
        public int Id { get; set; }

        [JsonPropertyName("source_url")]
        public string? SourceUrl { get; set; }

        [JsonPropertyName("mime_type")]
        public string? MimeType { get; set; }

        [JsonPropertyName("alt_text")]
        public string? AltText { get; set; }

        public WpRendered? Title { get; set; }
        public WpRendered? Caption { get; set; }
    }

    private sealed class WpPost
    {
        public int Id { get; set; }
        public string Slug { get; set; } = string.Empty;
        public int Parent { get; set; }
        public bool Sticky { get; set; }

        [JsonPropertyName("menu_order")]
        public int MenuOrder { get; set; }

        [JsonPropertyName("date_gmt")]
        public string? DateGmt { get; set; }

        [JsonPropertyName("modified_gmt")]
        public string? ModifiedGmt { get; set; }

        [JsonPropertyName("featured_media")]
        public int FeaturedMedia { get; set; }

        public WpRendered? Title { get; set; }
        public WpRendered? Content { get; set; }
        public WpRendered? Excerpt { get; set; }

        public List<int>? Categories { get; set; }
        public List<int>? Tags { get; set; }
    }
}
