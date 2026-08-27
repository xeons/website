using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Services;

public interface IDownloadService
{
    /// <summary>Creates an entry, streaming <paramref name="content"/> to disk as it arrives.</summary>
    Task<DownloadSaveResult> CreateAsync(Stream content, string fileName, string contentType,
        string? title = null, string? uploadedById = null, CancellationToken ct = default);

    /// <summary>Replaces the file behind an entry, keeping its slug, title and counters.</summary>
    Task<DownloadSaveResult> ReplaceFileAsync(int id, Stream content, string fileName,
        string contentType, CancellationToken ct = default);

    /// <summary>Removes the row and then the file.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);

    /// <summary>Returns null when no file is attached or the file is missing from disk.</summary>
    Task<DownloadFile?> OpenAsync(Download item, CancellationToken ct = default);

    /// <summary>Increments the transfer or blocked counter. Never throws.</summary>
    Task CountHitAsync(int id, bool blocked, CancellationToken ct = default);

    /// <summary>The stable public URL for the download.</summary>
    string PublicUrl(Download item);

    /// <summary>The stable public URL for a slug.</summary>
    string PublicUrl(string slug);
}
