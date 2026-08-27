using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Services;

public interface IMediaService
{
    Task<MediaUploadResult> SaveAsync(Stream content, string fileName, string contentType,
        string? uploadedById = null, string? sourceUrl = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Looks up a stored image from its public URL. Returns null for an external URL or one
    /// that does not match anything in the library.
    /// </summary>
    Task<MediaVariants?> ResolveByUrlAsync(string? url, CancellationToken ct = default);
    string PublicUrl(string? relativePath);
    string ThumbnailUrl(MediaItem item);
}
