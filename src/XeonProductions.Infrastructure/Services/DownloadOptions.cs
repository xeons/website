namespace XeonProductions.Infrastructure.Services;

public class DownloadOptions
{
    /// <summary>
    /// Absolute or content-root-relative directory holding the files. Must not be mounted as
    /// static files, and must stay outside the media root.
    /// </summary>
    public string StorageRoot { get; set; } = "downloads";

    /// <summary>URL prefix the gateway route is served from.</summary>
    public string PublicBasePath { get; set; } = "/download";

    /// <summary>Largest upload accepted, measured against the bytes that arrive.</summary>
    public long MaxFileSizeBytes { get; set; } = 2L * 1024 * 1024 * 1024;

    /// <summary>Chunk size used when streaming an upload to disk.</summary>
    public int BufferSize { get; set; } = 128 * 1024;
}
