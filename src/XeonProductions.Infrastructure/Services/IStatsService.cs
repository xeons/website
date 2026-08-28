using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Reads the page view table for the admin reports. Bots are excluded from every result.
/// </summary>
public interface IStatsService
{
    Task<StatsSummary> SummaryAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>One point per day across the range, including days with no traffic.</summary>
    Task<IReadOnlyList<StatsPoint>> SeriesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<IReadOnlyList<StatsBreakdown>> TopPagesAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);

    /// <summary>Referring hosts. A null key is direct traffic.</summary>
    Task<IReadOnlyList<StatsBreakdown>> TopReferrersAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);

    Task<IReadOnlyList<StatsBreakdown>> TopCountriesAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);
    Task<IReadOnlyList<StatsBreakdown>> TopBrowsersAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);
    Task<IReadOnlyList<StatsBreakdown>> TopOperatingSystemsAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);
    Task<IReadOnlyList<StatsBreakdown>> DevicesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    /// <summary>Pages a session started on, counted by session rather than by view.</summary>
    Task<IReadOnlyList<StatsBreakdown>> EntryPagesAsync(DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default);

    /// <summary>Views in the last few minutes, for the live count.</summary>
    Task<int> ActiveVisitorsAsync(TimeSpan window, CancellationToken ct = default);

    /// <summary>Writes a batch of captured views. Used by the background writer.</summary>
    Task WriteAsync(IReadOnlyList<PageView> views, CancellationToken ct = default);

    /// <summary>
    /// Applies dwell times reported by the beacon. Returns the ids that matched no row, so
    /// the caller can retry a beacon that overtook its own insert.
    /// </summary>
    Task<IReadOnlyList<Guid>> ApplyDurationsAsync(IReadOnlyDictionary<Guid, int> durations, CancellationToken ct = default);

    /// <summary>Deletes views older than the cutoff. Returns how many went.</summary>
    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
