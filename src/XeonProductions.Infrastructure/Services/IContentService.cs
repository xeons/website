using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Services;

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
