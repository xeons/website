namespace XeonProductions.Domain.Entities;

public class MediaItem
{
    public int Id { get; set; }

    /// <summary>Original upload name, kept for display only.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Path relative to the media root, e.g. 2026/08/screenshot.png</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Relative path of the generated thumbnail, when the item is an image.</summary>
    public string? ThumbnailPath { get; set; }

    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    public int? Width { get; set; }
    public int? Height { get; set; }

    public string? AltText { get; set; }
    public string? Caption { get; set; }
    public string? Title { get; set; }

    /// <summary>Original WordPress URL, so the importer can rewrite in-content image sources.</summary>
    public string? SourceUrl { get; set; }

    public string? UploadedById { get; set; }
    public ApplicationUser? UploadedBy { get; set; }

    public DateTimeOffset UploadedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsImage => ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}
