using Microsoft.EntityFrameworkCore;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Queries over the page view table.
///
/// Sessions, bounces and entry pages are derived here rather than stored, so there is one
/// table to write on the hot path and nothing that can fall out of step with it.
/// </summary>
public class StatsService(IDbContextFactory<AppDbContext> dbFactory) : IStatsService
{
    private static IQueryable<PageView> InRange(AppDbContext db, DateTimeOffset from, DateTimeOffset to) =>
        db.PageViews.AsNoTracking()
            .Where(v => v.ViewedAt >= from && v.ViewedAt < to && v.Device != DeviceType.Bot);

    public async Task<StatsSummary> SummaryAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var query = InRange(db, from, to);

        var views = await query.CountAsync(ct);
        if (views == 0) return StatsSummary.Empty;

        var visitors = await query.Select(v => v.VisitorHash).Distinct().CountAsync(ct);
        var sessions = await query.Select(v => v.SessionId).Distinct().CountAsync(ct);

        // A bounce is a session that never went anywhere else.
        var bounced = await query
            .GroupBy(v => v.SessionId)
            .CountAsync(g => g.Count() == 1, ct);

        // Only views the beacon reported carry a duration, so averaging over all of them
        // would quietly divide by the ones that never reported.
        var measured = query.Where(v => v.DurationSeconds > 0);
        var measuredCount = await measured.CountAsync(ct);

        var average = measuredCount == 0
            ? 0
            : await measured.AverageAsync(v => (double)v.DurationSeconds, ct);

        return new StatsSummary(views, visitors, sessions, bounced, average, measuredCount);
    }

    public async Task<IReadOnlyList<StatsPoint>> SeriesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await InRange(db, from, to)
            .GroupBy(v => v.ViewedAt.Date)
            .Select(g => new
            {
                Day = g.Key,
                Views = g.Count(),
                Visitors = g.Select(v => v.VisitorHash).Distinct().Count()
            })
            .ToListAsync(ct);

        var byDay = rows.ToDictionary(r => DateOnly.FromDateTime(r.Day));

        // Days with no traffic still need a point, or the chart implies activity across a gap.
        var points = new List<StatsPoint>();
        for (var day = DateOnly.FromDateTime(from.UtcDateTime);
             day < DateOnly.FromDateTime(to.UtcDateTime);
             day = day.AddDays(1))
        {
            points.Add(byDay.TryGetValue(day, out var row)
                ? new StatsPoint(day, row.Views, row.Visitors)
                : new StatsPoint(day, 0, 0));
        }

        return points;
    }

    public Task<IReadOnlyList<StatsBreakdown>> TopPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) =>
        BreakdownAsync(from, to, v => v.Path, take, ct);

    public Task<IReadOnlyList<StatsBreakdown>> TopReferrersAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) =>
        BreakdownAsync(from, to, v => v.ReferrerHost, take, ct);

    public Task<IReadOnlyList<StatsBreakdown>> TopCountriesAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) =>
        BreakdownAsync(from, to, v => v.CountryCode, take, ct);

    public Task<IReadOnlyList<StatsBreakdown>> TopBrowsersAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) =>
        BreakdownAsync(from, to, v => v.Browser, take, ct);

    public Task<IReadOnlyList<StatsBreakdown>> TopOperatingSystemsAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default) =>
        BreakdownAsync(from, to, v => v.OperatingSystem, take, ct);

    public async Task<IReadOnlyList<StatsBreakdown>> DevicesAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await InRange(db, from, to)
            .GroupBy(v => v.Device)
            .Select(g => new
            {
                Device = g.Key,
                Count = g.Count(),
                Average = g.Where(v => v.DurationSeconds > 0)
                    .Average(v => (double?)v.DurationSeconds) ?? 0
            })
            .OrderByDescending(r => r.Count)
            .ToListAsync(ct);

        return rows
            .Select(r => new StatsBreakdown(r.Device.ToString(), r.Count, r.Average))
            .ToList();
    }

    public async Task<IReadOnlyList<StatsBreakdown>> EntryPagesAsync(
        DateTimeOffset from, DateTimeOffset to, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await InRange(db, from, to)
            .Where(v => v.IsEntry)
            .GroupBy(v => v.Path)
            .Select(g => new { Path = g.Key, Count = g.Count() })
            .OrderByDescending(r => r.Count)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(r => new StatsBreakdown(r.Path, r.Count, 0)).ToList();
    }

    public async Task<int> ActiveVisitorsAsync(TimeSpan window, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var since = DateTimeOffset.UtcNow - window;

        return await db.PageViews.AsNoTracking()
            .Where(v => v.ViewedAt >= since && v.Device != DeviceType.Bot)
            .Select(v => v.VisitorHash)
            .Distinct()
            .CountAsync(ct);
    }

    public async Task WriteAsync(IReadOnlyList<PageView> views, CancellationToken ct = default)
    {
        if (views.Count == 0) return;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        db.PageViews.AddRange(views);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Guid>> ApplyDurationsAsync(
        IReadOnlyDictionary<Guid, int> durations, CancellationToken ct = default)
    {
        if (durations.Count == 0) return [];

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var ids = durations.Keys.ToList();

        var rows = await db.PageViews
            .Where(v => ids.Contains(v.ViewId))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            // Keep the longest report. A page can be hidden and shown again, and each
            // beacon reports the total so far.
            var reported = durations[row.ViewId];
            if (reported > row.DurationSeconds) row.DurationSeconds = reported;
        }

        await db.SaveChangesAsync(ct);

        var found = rows.Select(r => r.ViewId).ToHashSet();
        return ids.Where(id => !found.Contains(id)).ToList();
    }

    public async Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        return await db.PageViews
            .Where(v => v.ViewedAt < olderThan)
            .ExecuteDeleteAsync(ct);
    }

    private async Task<IReadOnlyList<StatsBreakdown>> BreakdownAsync(
        DateTimeOffset from, DateTimeOffset to,
        System.Linq.Expressions.Expression<Func<PageView, string?>> key,
        int take, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await InRange(db, from, to)
            .GroupBy(key)
            .Select(g => new
            {
                Key = g.Key,
                Count = g.Count(),
                Average = g.Where(v => v.DurationSeconds > 0)
                    .Average(v => (double?)v.DurationSeconds) ?? 0
            })
            .OrderByDescending(r => r.Count)
            .Take(take)
            .ToListAsync(ct);

        return rows.Select(r => new StatsBreakdown(r.Key, r.Count, r.Average)).ToList();
    }
}
