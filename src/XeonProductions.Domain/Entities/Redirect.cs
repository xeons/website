namespace XeonProductions.Domain.Entities;

/// <summary>
/// Preserves inbound links from the WordPress URL structure. Matched before the 404 handler runs.
/// </summary>
public class Redirect
{
    public int Id { get; set; }

    /// <summary>Site-relative source path, normalised to lowercase with no trailing slash.</summary>
    public string FromPath { get; set; } = string.Empty;

    public string ToUrl { get; set; } = string.Empty;

    /// <summary>301 permanent or 302 temporary.</summary>
    public int StatusCode { get; set; } = 301;

    public bool IsActive { get; set; } = true;

    public int HitCount { get; set; }
    public DateTimeOffset? LastHitAt { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
