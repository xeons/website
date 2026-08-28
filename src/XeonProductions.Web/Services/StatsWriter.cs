using Microsoft.Extensions.Options;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Services;

/// <summary>
/// Drains the recorder's queues into the database in batches, and prunes old rows once a day.
///
/// Durations are applied after inserts within the same cycle, because a visitor who leaves
/// immediately can have their beacon overtake the insert it refers to. Anything that still
/// matches no row is retried on later cycles for a short while before being given up on.
/// </summary>
public class StatsWriter(
    StatsRecorder recorder,
    IServiceScopeFactory scopeFactory,
    IOptions<StatsOptions> options,
    ILogger<StatsWriter> logger) : BackgroundService
{
    private const int MaxBatch = 500;

    /// <summary>Cycles a duration is retried for before the view it names is written off.</summary>
    private const int MaxDurationRetries = 5;

    private readonly StatsOptions _opts = options.Value;
    private readonly Dictionary<Guid, (int Seconds, int Attempts)> _pending = [];

    private DateOnly _lastPrune = DateOnly.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(250, _opts.FlushIntervalMs));

        logger.LogInformation("Statistics writer started, flushing every {Interval}.", interval);

        using var timer = new PeriodicTimer(interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await FlushAsync(stoppingToken);
                await PruneIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed flush must not take the writer down, or the site stops counting
                // until it is restarted.
                logger.LogError(ex, "Statistics flush failed.");
            }
        }

        // Best effort on the way out, so a graceful stop does not lose the last few seconds.
        try
        {
            await FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Final statistics flush failed.");
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var views = Drain(recorder.Views, MaxBatch);
        var reported = DrainDurations();

        if (views.Count == 0 && reported.Count == 0 && _pending.Count == 0) return;

        using var scope = scopeFactory.CreateScope();
        var stats = scope.ServiceProvider.GetRequiredService<IStatsService>();

        if (views.Count > 0)
        {
            await stats.WriteAsync(views, ct);
        }

        foreach (var (id, seconds) in reported)
        {
            _pending[id] = (Math.Max(seconds, _pending.TryGetValue(id, out var p) ? p.Seconds : 0),
                            0);
        }

        if (_pending.Count == 0) return;

        var batch = _pending.ToDictionary(kv => kv.Key, kv => kv.Value.Seconds);
        var unmatched = await stats.ApplyDurationsAsync(batch, ct);

        var missing = unmatched.ToHashSet();

        foreach (var id in batch.Keys)
        {
            if (!missing.Contains(id))
            {
                _pending.Remove(id);
                continue;
            }

            var entry = _pending[id];

            if (entry.Attempts + 1 >= MaxDurationRetries)
            {
                _pending.Remove(id);
            }
            else
            {
                _pending[id] = (entry.Seconds, entry.Attempts + 1);
            }
        }
    }

    private static List<PageView> Drain(
        System.Threading.Channels.ChannelReader<PageView> reader, int max)
    {
        var batch = new List<PageView>();

        while (batch.Count < max && reader.TryRead(out var view)) batch.Add(view);

        return batch;
    }

    private List<(Guid Id, int Seconds)> DrainDurations()
    {
        var batch = new List<(Guid, int)>();

        while (batch.Count < MaxBatch && recorder.Durations.TryRead(out var item))
        {
            batch.Add(item);
        }

        return batch;
    }

    private async Task PruneIfDueAsync(CancellationToken ct)
    {
        if (_opts.RetentionDays <= 0) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today == _lastPrune) return;

        _lastPrune = today;

        using var scope = scopeFactory.CreateScope();
        var stats = scope.ServiceProvider.GetRequiredService<IStatsService>();

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_opts.RetentionDays);
        var removed = await stats.PruneAsync(cutoff, ct);

        if (removed > 0)
        {
            logger.LogInformation(
                "Pruned {Count} page views older than {Days} days.", removed, _opts.RetentionDays);
        }
    }
}
