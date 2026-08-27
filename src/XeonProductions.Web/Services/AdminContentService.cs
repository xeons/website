using Microsoft.EntityFrameworkCore;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Services;

/// <summary>
/// The write side of the CMS. Slug generation, sanitising and taxonomy wiring all live here so
/// the admin components stay declarative.
///
/// Each call opens its own <see cref="AppDbContext"/>. An interactive circuit can live for
/// hours, and a context that old would serve stale tracked entities.
/// </summary>
public class AdminContentService(
    IDbContextFactory<AppDbContext> dbFactory,
    IHtmlService html,
    INavigationService navigation,
    IRedirectMap redirects)
{
    // --- Posts ---

    public async Task<Post> SavePostAsync(
        Post edited, IEnumerable<int> categoryIds, IEnumerable<string> tagNames,
        string? authorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var isNew = edited.Id == 0;

        var post = isNew
            ? new Post { CreatedAt = DateTimeOffset.UtcNow, AuthorId = authorId }
            : await db.Posts
                .Include(p => p.Categories)
                .Include(p => p.Tags)
                .FirstAsync(p => p.Id == edited.Id, ct);

        post.Title = edited.Title.Trim();
        post.ContentHtml = html.Sanitize(edited.ContentHtml);
        post.Excerpt = string.IsNullOrWhiteSpace(edited.Excerpt)
            ? html.BuildExcerpt(post.ContentHtml)
            : edited.Excerpt.Trim();

        var baseSlug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(edited.Slug) ? post.Title : edited.Slug);

        post.Slug = await SlugHelper.MakeUniqueAsync(baseSlug, async candidate =>
            await db.Posts.AnyAsync(p => p.Slug == candidate && p.Id != post.Id, ct));

        post.Status = edited.Status;
        post.IsSticky = edited.IsSticky;
        post.AllowComments = edited.AllowComments;
        post.FeaturedImageId = edited.FeaturedImageId;

        post.SeoTitle = Trim(edited.SeoTitle);
        post.SeoDescription = Trim(edited.SeoDescription);
        post.CanonicalUrl = Trim(edited.CanonicalUrl);
        post.SocialImageUrl = Trim(edited.SocialImageUrl);
        post.NoIndex = edited.NoIndex;

        // Publishing for the first time stamps "now" unless a date was chosen explicitly.
        if (post.Status is ContentStatus.Published or ContentStatus.Scheduled)
        {
            post.PublishedAt = edited.PublishedAt ?? post.PublishedAt ?? DateTimeOffset.UtcNow;
        }
        else
        {
            post.PublishedAt = edited.PublishedAt;
        }

        post.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew) db.Posts.Add(post);

        await SyncCategoriesAsync(db, post, categoryIds, ct);
        await SyncTagsAsync(db, post, tagNames, ct);

        await db.SaveChangesAsync(ct);
        return post;
    }

    public async Task DeletePostAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var post = await db.Posts.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (post is null) return;

        db.Posts.Remove(post);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SyncCategoriesAsync(
        AppDbContext db, Post post, IEnumerable<int> categoryIds, CancellationToken ct)
    {
        var ids = categoryIds.Distinct().ToList();
        var categories = await db.Categories.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

        post.Categories.Clear();
        post.Categories.AddRange(categories);
    }

    private static async Task SyncTagsAsync(
        AppDbContext db, Post post, IEnumerable<string> tagNames, CancellationToken ct)
    {
        var names = tagNames
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .DistinctBy(t => t.ToLowerInvariant())
            .ToList();

        post.Tags.Clear();
        if (names.Count == 0) return;

        var bySlug = new Dictionary<string, string>();
        foreach (var name in names)
        {
            var slug = SlugHelper.Slugify(name);
            if (slug.Length > 0) bySlug.TryAdd(slug, name);
        }

        var slugList = bySlug.Keys.ToList();
        var existing = await db.Tags.Where(t => slugList.Contains(t.Slug)).ToListAsync(ct);
        post.Tags.AddRange(existing);

        // Tags are created on the fly, the way the WordPress tag box behaved.
        foreach (var (slug, name) in bySlug)
        {
            if (existing.Any(t => t.Slug == slug)) continue;

            var tag = new Tag { Name = name, Slug = slug };
            db.Tags.Add(tag);
            post.Tags.Add(tag);
        }
    }

    // --- Pages ---

    public async Task<Page> SavePageAsync(Page edited, string? authorId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var isNew = edited.Id == 0;

        var page = isNew
            ? new Page { CreatedAt = DateTimeOffset.UtcNow, AuthorId = authorId }
            : await db.Pages.FirstAsync(p => p.Id == edited.Id, ct);

        page.Title = edited.Title.Trim();
        page.ContentHtml = html.Sanitize(edited.ContentHtml);
        page.Status = edited.Status;
        page.Template = edited.Template;
        page.MenuOrder = edited.MenuOrder;
        page.ShowTitle = edited.ShowTitle;
        page.FeaturedImageId = edited.FeaturedImageId;

        page.ParentId = await ResolveParentAsync(db, edited, ct);

        var baseSlug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(edited.Slug) ? page.Title : edited.Slug);

        var parentId = page.ParentId;

        // The null case is spelled out: "p.ParentId == parentId" with a null parameter
        // translates to "ParentId = NULL", which never matches a top-level sibling.
        page.Slug = await SlugHelper.MakeUniqueAsync(baseSlug, async candidate =>
            await db.Pages.AnyAsync(
                p => p.Slug == candidate
                     && p.Id != page.Id
                     && (parentId == null ? p.ParentId == null : p.ParentId == parentId),
                ct));

        page.SeoTitle = Trim(edited.SeoTitle);
        page.SeoDescription = Trim(edited.SeoDescription);
        page.CanonicalUrl = Trim(edited.CanonicalUrl);
        page.SocialImageUrl = Trim(edited.SocialImageUrl);
        page.NoIndex = edited.NoIndex;

        if (page.Status == ContentStatus.Published)
        {
            page.PublishedAt ??= DateTimeOffset.UtcNow;
        }

        page.UpdatedAt = DateTimeOffset.UtcNow;

        if (isNew) db.Pages.Add(page);

        await db.SaveChangesAsync(ct);
        return page;
    }

    /// <summary>
    /// Rejects a parent choice that would create a cycle, which would otherwise hang the
    /// path resolver.
    /// </summary>
    private static async Task<int?> ResolveParentAsync(AppDbContext db, Page edited, CancellationToken ct)
    {
        if (edited.ParentId is not int parentId) return null;
        if (edited.Id != 0 && parentId == edited.Id) return null;
        if (edited.Id == 0) return parentId;

        var lookup = await db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.ParentId })
            .ToDictionaryAsync(p => p.Id, p => p.ParentId, ct);

        var cursor = (int?)parentId;
        var guard = 0;

        while (cursor is int current && guard++ < 50)
        {
            if (current == edited.Id) return null;
            cursor = lookup.GetValueOrDefault(current);
        }

        return parentId;
    }

    public async Task DeletePageAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var page = await db.Pages.Include(p => p.Children).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (page is null) return;

        // Promote the children rather than cascading a whole branch out of existence.
        foreach (var child in page.Children)
        {
            child.ParentId = page.ParentId;
        }

        db.Pages.Remove(page);
        await db.SaveChangesAsync(ct);
    }

    // --- Taxonomy ---

    public async Task<Category> SaveCategoryAsync(Category edited, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var isNew = edited.Id == 0;
        var category = isNew ? new Category() : await db.Categories.FirstAsync(c => c.Id == edited.Id, ct);

        category.Name = edited.Name.Trim();
        category.Description = Trim(edited.Description);
        category.SortOrder = edited.SortOrder;
        category.ParentId = edited.ParentId == category.Id ? null : edited.ParentId;

        var baseSlug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(edited.Slug) ? category.Name : edited.Slug);

        category.Slug = await SlugHelper.MakeUniqueAsync(baseSlug, async candidate =>
            await db.Categories.AnyAsync(c => c.Slug == candidate && c.Id != category.Id, ct));

        if (isNew) db.Categories.Add(category);

        await db.SaveChangesAsync(ct);
        return category;
    }

    public async Task<bool> DeleteCategoryAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var category = await db.Categories
            .Include(c => c.Children)
            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (category is null) return false;

        foreach (var child in category.Children)
        {
            child.ParentId = category.ParentId;
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<Tag> SaveTagAsync(Tag edited, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var isNew = edited.Id == 0;
        var tag = isNew ? new Tag() : await db.Tags.FirstAsync(t => t.Id == edited.Id, ct);

        tag.Name = edited.Name.Trim();
        tag.Description = Trim(edited.Description);

        var baseSlug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(edited.Slug) ? tag.Name : edited.Slug);

        tag.Slug = await SlugHelper.MakeUniqueAsync(baseSlug, async candidate =>
            await db.Tags.AnyAsync(t => t.Slug == candidate && t.Id != tag.Id, ct));

        if (isNew) db.Tags.Add(tag);

        await db.SaveChangesAsync(ct);
        return tag;
    }

    public async Task DeleteTagAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var tag = await db.Tags.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tag is null) return;

        db.Tags.Remove(tag);
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteUnusedTagsAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var orphans = await db.Tags.Where(t => !t.Posts.Any()).ToListAsync(ct);
        if (orphans.Count == 0) return 0;

        db.Tags.RemoveRange(orphans);
        await db.SaveChangesAsync(ct);
        return orphans.Count;
    }

    // --- Menus and widgets ---

    public async Task SaveMenuItemAsync(MenuItem edited, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (edited.Id == 0)
        {
            db.MenuItems.Add(new MenuItem
            {
                MenuId = edited.MenuId,
                Label = edited.Label.Trim(),
                Url = edited.Url.Trim(),
                SortOrder = edited.SortOrder,
                OpenInNewTab = edited.OpenInNewTab,
                ParentId = edited.ParentId,
                CssClass = Trim(edited.CssClass),
                IconSvg = edited.IconSvg is null ? null : html.Sanitize(edited.IconSvg)
            });
        }
        else
        {
            var item = await db.MenuItems.FirstAsync(m => m.Id == edited.Id, ct);
            item.Label = edited.Label.Trim();
            item.Url = edited.Url.Trim();
            item.SortOrder = edited.SortOrder;
            item.OpenInNewTab = edited.OpenInNewTab;
            item.ParentId = edited.ParentId == item.Id ? null : edited.ParentId;
            item.CssClass = Trim(edited.CssClass);
            item.IconSvg = edited.IconSvg is null ? null : html.Sanitize(edited.IconSvg);
        }

        await db.SaveChangesAsync(ct);
        navigation.Invalidate();
    }

    public async Task DeleteMenuItemAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var item = await db.MenuItems.Include(m => m.Children).FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return;

        foreach (var child in item.Children)
        {
            child.ParentId = item.ParentId;
        }

        db.MenuItems.Remove(item);
        await db.SaveChangesAsync(ct);
        navigation.Invalidate();
    }

    public async Task SaveWidgetAsync(Widget edited, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        if (edited.Id == 0)
        {
            db.Widgets.Add(new Widget
            {
                Title = edited.Title.Trim(),
                Type = edited.Type,
                Area = edited.Area,
                SortOrder = edited.SortOrder,
                IsActive = edited.IsActive,
                MaxItems = Math.Clamp(edited.MaxItems, 1, 50),
                HtmlContent = edited.HtmlContent is null ? null : html.Sanitize(edited.HtmlContent),
                FeedUrl = Trim(edited.FeedUrl),
                ShowFeedDates = edited.ShowFeedDates,
                Links = edited.Links.Select(CleanLink).ToList()
            });
        }
        else
        {
            var widget = await db.Widgets.Include(w => w.Links).FirstAsync(w => w.Id == edited.Id, ct);

            widget.Title = edited.Title.Trim();
            widget.Type = edited.Type;
            widget.Area = edited.Area;
            widget.SortOrder = edited.SortOrder;
            widget.IsActive = edited.IsActive;
            widget.MaxItems = Math.Clamp(edited.MaxItems, 1, 50);
            widget.HtmlContent = edited.HtmlContent is null ? null : html.Sanitize(edited.HtmlContent);
            widget.FeedUrl = Trim(edited.FeedUrl);
            widget.ShowFeedDates = edited.ShowFeedDates;

            var keptIds = edited.Links.Where(l => l.Id != 0).Select(l => l.Id).ToHashSet();
            db.WidgetLinks.RemoveRange(widget.Links.Where(l => !keptIds.Contains(l.Id)));

            foreach (var link in edited.Links)
            {
                var existing = link.Id == 0 ? null : widget.Links.FirstOrDefault(l => l.Id == link.Id);

                if (existing is null)
                {
                    widget.Links.Add(CleanLink(link));
                }
                else
                {
                    existing.Label = link.Label.Trim();
                    existing.Url = link.Url.Trim();
                    existing.Description = Trim(link.Description);
                    existing.SortOrder = link.SortOrder;
                    existing.OpenInNewTab = link.OpenInNewTab;
                    existing.IsSponsored = link.IsSponsored;
                }
            }
        }

        await db.SaveChangesAsync(ct);
        navigation.Invalidate();
    }

    private static WidgetLink CleanLink(WidgetLink link) => new()
    {
        Label = link.Label.Trim(),
        Url = link.Url.Trim(),
        Description = Trim(link.Description),
        SortOrder = link.SortOrder,
        OpenInNewTab = link.OpenInNewTab,
        IsSponsored = link.IsSponsored
    };

    public async Task DeleteWidgetAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var widget = await db.Widgets.FirstOrDefaultAsync(w => w.Id == id, ct);
        if (widget is null) return;

        db.Widgets.Remove(widget);
        await db.SaveChangesAsync(ct);
        navigation.Invalidate();
    }

    // --- Downloads ---

    /// <summary>
    /// Saves the metadata around a stored file. The file itself is replaced through the
    /// upload endpoint, not here.
    /// </summary>
    public async Task<Download> SaveDownloadAsync(Download edited, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var item = await db.Downloads.FirstAsync(d => d.Id == edited.Id, ct);

        item.Title = edited.Title.Trim();
        item.Description = Trim(edited.Description);
        item.Version = Trim(edited.Version);
        item.IsPublished = edited.IsPublished;
        item.RequiresAuthentication = edited.RequiresAuthentication;
        item.ProtectionOverride = edited.ProtectionOverride;
        item.AllowedReferrers = Trim(edited.AllowedReferrers);

        // Reaches a Content-Disposition header, so it is cleaned the same way an uploaded
        // name is.
        if (!string.IsNullOrWhiteSpace(edited.FileName))
        {
            item.FileName = DownloadService.SafeFileName(edited.FileName);
        }

        var baseSlug = SlugHelper.Slugify(
            string.IsNullOrWhiteSpace(edited.Slug) ? item.Title : edited.Slug);

        item.Slug = await SlugHelper.MakeUniqueAsync(baseSlug, async candidate =>
            await db.Downloads.AnyAsync(d => d.Slug == candidate && d.Id != item.Id, ct));

        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        return item;
    }

    // --- Redirects ---

    public async Task SaveRedirectAsync(Redirect edited, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var normalized = NormalizePath(edited.FromPath);

        var redirect = edited.Id == 0
            ? new Redirect()
            : await db.Redirects.FirstAsync(r => r.Id == edited.Id, ct);

        redirect.FromPath = normalized;
        redirect.ToUrl = edited.ToUrl.Trim();
        redirect.StatusCode = edited.StatusCode == 302 ? 302 : 301;
        redirect.IsActive = edited.IsActive;
        redirect.Notes = Trim(edited.Notes);

        if (edited.Id == 0) db.Redirects.Add(redirect);

        await db.SaveChangesAsync(ct);
        redirects.Invalidate();
    }

    public async Task DeleteRedirectAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var redirect = await db.Redirects.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (redirect is null) return;

        db.Redirects.Remove(redirect);
        await db.SaveChangesAsync(ct);
        redirects.Invalidate();
    }

    /// <summary>Matches the normalisation the redirect middleware applies to inbound paths.</summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        var trimmed = path.Trim();

        // Accept a pasted absolute URL and keep only its path.
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute))
        {
            trimmed = absolute.AbsolutePath;
        }

        return "/" + trimmed.Trim('/').ToLowerInvariant();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
