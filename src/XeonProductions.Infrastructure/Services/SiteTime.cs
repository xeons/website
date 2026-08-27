namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// The site's own timezone, which is not the server's.
///
/// This matters more than it looks. WordPress built permalinks from the site's local date,
/// so a post published at 19:18 on 16 March in America/Chicago lives at /2024/03/16/... even
/// though that instant is 17 March in UTC. A container runs in UTC, so deriving the permalink
/// from the server clock would silently move a quarter of the archive to a different URL and
/// break every inbound link to it.
///
/// Held statically because permalink formatting happens in static helpers and in components
/// that have no reason to depend on the settings service. It is written once at startup and
/// again whenever the setting changes, and read on every request.
/// </summary>
public static class SiteTime
{
    private static TimeZoneInfo _zone = ResolveOrUtc("America/Chicago");

    public static TimeZoneInfo Zone => Volatile.Read(ref _zone);

    /// <summary>Applies a new timezone. An unknown id is ignored rather than throwing.</summary>
    public static void Configure(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return;

        var resolved = Resolve(timeZoneId);
        if (resolved is not null) Volatile.Write(ref _zone, resolved);
    }

    /// <summary>Converts a stored instant into the site's wall-clock time.</summary>
    public static DateTimeOffset ToSiteTime(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, Zone);

    public static DateTimeOffset? ToSiteTime(DateTimeOffset? instant) =>
        instant is null ? null : ToSiteTime(instant.Value);

    private static TimeZoneInfo ResolveOrUtc(string id) => Resolve(id) ?? TimeZoneInfo.Utc;

    private static TimeZoneInfo? Resolve(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            // Windows and Linux disagree on identifiers, and a container may lack tzdata.
            // Falling back is always better than failing to render the site.
            return TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var windowsId)
                   && windowsId is not null
                ? TryFind(windowsId)
                : null;
        }
    }

    private static TimeZoneInfo? TryFind(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
