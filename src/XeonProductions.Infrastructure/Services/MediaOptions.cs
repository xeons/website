namespace XeonProductions.Infrastructure.Services;

public class MediaOptions
{
    /// <summary>Absolute or content-root-relative directory that holds uploaded files.</summary>
    public string StorageRoot { get; set; } = "media";

    /// <summary>URL prefix the stored files are served from.</summary>
    public string PublicBasePath { get; set; } = "/media";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;
    public int ThumbnailWidth { get; set; } = 480;

    /// <summary>Images wider than this are downscaled on upload.</summary>
    public int MaxImageWidth { get; set; } = 2000;

    /// <summary>Quality for generated WebP thumbnails, 1 to 100.</summary>
    public int ThumbnailQuality { get; set; } = 82;

    /// <summary>
    /// Widths, besides the image's own, to offer as WebP. Only those narrower than the
    /// stored image are written; a full width copy is always written alongside them, which
    /// is where most of the saving is. A photographic PNG costs several times its WebP.
    /// </summary>
    public int[] VariantWidths { get; set; } = [800, 1200];

    /// <summary>Quality for the generated WebP variants, 1 to 100.</summary>
    public int VariantQuality { get; set; } = 90;

    public string[] AllowedExtensions { get; set; } =
    [
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".avif", ".svg", ".ico",
        ".pdf", ".txt", ".zip", ".json", ".xml", ".csv", ".mp4", ".webm"
    ];
}
