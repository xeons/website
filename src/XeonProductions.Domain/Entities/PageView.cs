using XeonProductions.Domain.Enums;

namespace XeonProductions.Domain.Entities;

/// <summary>
/// One page request. Sessions, bounce rate and entry and exit pages are derived from these
/// rows at query time rather than being stored separately.
/// </summary>
public class PageView
{
    public long Id { get; set; }

    /// <summary>
    /// Sent to the browser so the beacon can report dwell time against this row. Generated
    /// before the row is written, because the write happens off the request.
    /// </summary>
    public Guid ViewId { get; set; }

    /// <summary>Path only, without the query string.</summary>
    public string Path { get; set; } = string.Empty;

    public DateTimeOffset ViewedAt { get; set; }

    /// <summary>Groups views into a visit. Derived from the visitor hash and a time window.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Irreversible hash of address, user agent and a salt that rotates daily. Not an
    /// identity: the same visitor gets a different value tomorrow.
    /// </summary>
    public string VisitorHash { get; set; } = string.Empty;

    /// <summary>Host of the referring URL, or null when the visit was direct.</summary>
    public string? ReferrerHost { get; set; }

    /// <summary>Full referring URL, kept for inspecting where a link was placed.</summary>
    public string? ReferrerUrl { get; set; }

    /// <summary>ISO 3166-1 alpha-2, or null when no database is configured.</summary>
    public string? CountryCode { get; set; }

    public string? Browser { get; set; }
    public string? OperatingSystem { get; set; }
    public DeviceType Device { get; set; }

    /// <summary>Seconds the page was open, reported by the beacon. Zero until it arrives.</summary>
    public int DurationSeconds { get; set; }

    /// <summary>True when this was the first view of its session.</summary>
    public bool IsEntry { get; set; }
}
