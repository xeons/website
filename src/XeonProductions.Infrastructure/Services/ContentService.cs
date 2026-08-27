using Microsoft.EntityFrameworkCore;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}

public interface IContentService
{
    Task<PagedResult<Post>> GetPublishedPostsAsync(int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<Post>> GetPostsByCategoryAsync(string slug, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<Post>> GetPostsByTagAsync(string slug, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<Post>> GetPostsByAuthorAsync(string authorSlug, int page, int pageSize, CancellationToken ct = default);
    Task<PagedResult<Post>> SearchPostsAsync(string query, int page, int pageSize, CancellationToken ct = default);
    Task<Post?> GetPostBySlugAsync(string slug, bool includeUnpublished = false, CancellationToken ct = default);
    Task<(Post? Previous, Post? Next)> GetAdjacentPostsAsync(Post post, CancellationToken ct = default);
    Task<IReadOnlyList<Post>> GetRecentPostsAsync(int count, CancellationToken ct = default);
    Task<IReadOnlyList<Post>> GetRelatedPostsAsync(Post post, int count, CancellationToken ct = default);

    Task<Page?> GetPageByPathAsync(string path, bool includeUnpublished = false, CancellationToken ct = default);
    Task<string> GetPagePathAsync(int pageId, CancellationToken ct = default);
    Task<IReadOnlyList<Page>> GetChildPagesAsync(int parentId, CancellationToken ct = default);
}

/// <summary>
/// Read-side queries for the public site.
///
/// Every method opens its own context. Blazor renders a page's components concurrently during
/// static server rendering, so a layout and its page routinely query at the same moment; a
/// single shared context throws the moment two of those overlap.
/// </summary>
public class ContentService(IDbContextFactory<AppDbContext> dbFactory) : IContentService
{
    private static IQueryable<Post> VisiblePosts(AppDbContext db) =>
        db.Posts.AsNoTracking()
            .Where(p => p.Status == ContentStatus.Published && p.PublishedAt <= DateTimeOffset.UtcNow);

    public async Task<PagedResult<Post>> GetPublishedPostsAsync(int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = VisiblePosts(db)
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .AsSplitQuery()
            // Sticky posts float to the top of the index, newest first within each group.
            .OrderByDescending(p => p.IsSticky)
            .ThenByDescending(p => p.PublishedAt);

        return await PageAsync(query, page, pageSize, ct);
    }

    public async Task<PagedResult<Post>> GetPostsByCategoryAsync(string slug, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = VisiblePosts(db)
            .Where(p => p.Categories.Any(c => c.Slug == slug))
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .AsSplitQuery()
            .OrderByDescending(p => p.PublishedAt);

        return await PageAsync(query, page, pageSize, ct);
    }

    public async Task<PagedResult<Post>> GetPostsByTagAsync(string slug, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = VisiblePosts(db)
            .Where(p => p.Tags.Any(t => t.Slug == slug))
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .AsSplitQuery()
            .OrderByDescending(p => p.PublishedAt);

        return await PageAsync(query, page, pageSize, ct);
    }

    public async Task<PagedResult<Post>> GetPostsByAuthorAsync(string authorSlug, int page, int pageSize, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = VisiblePosts(db)
            .Where(p => p.Author != null && p.Author.Slug == authorSlug)
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .AsSplitQuery()
            .OrderByDescending(p => p.PublishedAt);

        return await PageAsync(query, page, pageSize, ct);
    }

    public async Task<PagedResult<Post>> SearchPostsAsync(string query, int page, int pageSize, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PagedResult<Post>([], page, pageSize, 0);
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var term = $"%{query.Trim()}%";

        // ILIKE keeps this case-insensitive on Postgres without a functional index.
        var results = VisiblePosts(db)
            .Where(p => EF.Functions.ILike(p.Title, term)
                     || EF.Functions.ILike(p.ContentHtml, term)
                     || (p.Excerpt != null && EF.Functions.ILike(p.Excerpt, term)))
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .AsSplitQuery()
            // A title match outranks a body match.
            .OrderByDescending(p => EF.Functions.ILike(p.Title, term))
            .ThenByDescending(p => p.PublishedAt);

        return await PageAsync(results, page, pageSize, ct);
    }

    public async Task<Post?> GetPostBySlugAsync(string slug, bool includeUnpublished = false, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = includeUnpublished ? db.Posts.AsNoTracking() : VisiblePosts(db);

        return await query
            .Include(p => p.Categories)
            .Include(p => p.Tags)
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .FirstOrDefaultAsync(p => p.Slug == slug, ct);
    }

    public async Task<(Post? Previous, Post? Next)> GetAdjacentPostsAsync(Post post, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var previous = await VisiblePosts(db)
            .Where(p => p.PublishedAt < post.PublishedAt)
            .OrderByDescending(p => p.PublishedAt)
            .FirstOrDefaultAsync(ct);

        var next = await VisiblePosts(db)
            .Where(p => p.PublishedAt > post.PublishedAt)
            .OrderBy(p => p.PublishedAt)
            .FirstOrDefaultAsync(ct);

        return (previous, next);
    }

    public async Task<IReadOnlyList<Post>> GetRecentPostsAsync(int count, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await VisiblePosts(db)
            .OrderByDescending(p => p.PublishedAt)
            .Take(count)
            .Include(p => p.FeaturedImage)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Post>> GetRelatedPostsAsync(Post post, int count, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var categoryIds = post.Categories.Select(c => c.Id).ToList();
        var tagIds = post.Tags.Select(t => t.Id).ToList();

        // Rank by shared taxonomy terms, then recency.
        var related = await VisiblePosts(db)
            .Where(p => p.Id != post.Id
                     && (p.Categories.Any(c => categoryIds.Contains(c.Id))
                      || p.Tags.Any(t => tagIds.Contains(t.Id))))
            .Include(p => p.FeaturedImage)
            .OrderByDescending(p => p.Tags.Count(t => tagIds.Contains(t.Id))
                                  + p.Categories.Count(c => categoryIds.Contains(c.Id)))
            .ThenByDescending(p => p.PublishedAt)
            .Take(count)
            .ToListAsync(ct);

        if (related.Count >= count) return related;

        // Backfill with recent posts so the block is never half empty.
        var exclude = related.Select(p => p.Id).Append(post.Id).ToList();

        var filler = await VisiblePosts(db)
            .Where(p => !exclude.Contains(p.Id))
            .Include(p => p.FeaturedImage)
            .OrderByDescending(p => p.PublishedAt)
            .Take(count - related.Count)
            .ToListAsync(ct);

        related.AddRange(filler);
        return related;
    }

    public async Task<Page?> GetPageByPathAsync(string path, bool includeUnpublished = false, CancellationToken ct = default)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Walk the parent chain one segment at a time so /snippets/foo cannot match a
        // top-level page that happens to share the slug "foo".
        int? parentId = null;
        Page? current = null;

        foreach (var segment in segments)
        {
            var pid = parentId;

            current = await db.Pages.AsNoTracking()
                .FirstOrDefaultAsync(
                    p => (pid == null ? p.ParentId == null : p.ParentId == pid) && p.Slug == segment,
                    ct);

            if (current is null) return null;
            parentId = current.Id;
        }

        if (current is null) return null;
        if (!includeUnpublished && !current.IsVisible) return null;

        return await db.Pages.AsNoTracking()
            .Include(p => p.FeaturedImage)
            .Include(p => p.Author)
            .Include(p => p.Children.Where(c => c.Status == ContentStatus.Published)
                                    .OrderBy(c => c.MenuOrder).ThenBy(c => c.Title))
            .FirstOrDefaultAsync(p => p.Id == current.Id, ct);
    }

    public async Task<string> GetPagePathAsync(int pageId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var lookup = await db.Pages.AsNoTracking()
            .Select(p => new { p.Id, p.Slug, p.ParentId })
            .ToDictionaryAsync(p => p.Id, ct);

        var segments = new List<string>();
        var cursor = pageId;
        var guard = 0;

        while (lookup.TryGetValue(cursor, out var node) && guard++ < 20)
        {
            segments.Insert(0, node.Slug);
            if (node.ParentId is null) break;
            cursor = node.ParentId.Value;
        }

        return "/" + string.Join('/', segments);
    }

    public async Task<IReadOnlyList<Page>> GetChildPagesAsync(int parentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.Pages.AsNoTracking()
            .Where(p => p.ParentId == parentId && p.Status == ContentStatus.Published)
            .OrderBy(p => p.MenuOrder).ThenBy(p => p.Title)
            .ToListAsync(ct);
    }

    private static async Task<PagedResult<Post>> PageAsync(
        IQueryable<Post> query, int page, int pageSize, CancellationToken ct)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return new PagedResult<Post>(items, page, pageSize, total);
    }
}
