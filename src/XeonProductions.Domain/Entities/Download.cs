using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

/// <summary>
/// A binary offered for download. The file has no static URL; it is served only through the
/// download endpoints.
/// </summary>
public class Download
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>The public URL segment, <c>/download/{slug}</c>.</summary>
    public string Slug { get; set; } = string.Empty;

    /// <summary>Plain text, shown in the admin only.</summary>
    public string? Description { get; set; }

    /// <summary>Free text such as "1.4.2". Shown in the admin only.</summary>
    public string? Version { get; set; }

    /// <summary>The name the browser is offered.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Path relative to the downloads storage root. Carries a random component so the layout
    /// is not guessable.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>The type the uploader declared. Recorded for display, never used to serve.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>Lowercase hex SHA-256, computed while the upload streams to disk.</summary>
    public string? Sha256 { get; set; }

    /// <summary>When false the download returns 404 and the file is kept.</summary>
    public bool IsPublished { get; set; } = true;

    /// <summary>Restricts the file to signed-in accounts.</summary>
    public bool RequiresAuthentication { get; set; }

    /// <summary>Null follows the site-wide setting.</summary>
    public HotlinkProtection? ProtectionOverride { get; set; }

    /// <summary>
    /// Extra referrer hosts allowed for this file, comma or newline separated, in addition
    /// to the site-wide list.
    /// </summary>
    public string? AllowedReferrers { get; set; }

    /// <summary>Transfers started. A resumed transfer does not count again.</summary>
    public long DownloadCount { get; set; }

    /// <summary>Requests refused by the referrer check.</summary>
    public long BlockedCount { get; set; }

    public DateTimeOffset? LastDownloadedAt { get; set; }

    public string? UploadedById { get; set; }
    public ApplicationUser? UploadedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>True once a file has been attached.</summary>
    public bool HasFile => !string.IsNullOrEmpty(RelativePath);
}
