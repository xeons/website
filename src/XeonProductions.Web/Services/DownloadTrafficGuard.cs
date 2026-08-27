namespace XeonProductions.Web.Services;

/// <summary>
/// Per-client limits on transfers started and transfers in flight. State is held in memory
/// and is per instance.
/// </summary>
public class DownloadTrafficGuard : IDownloadTrafficGuard
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    /// <summary>Abandoned entries are swept once the map reaches this size.</summary>
    private const int SweepThreshold = 512;

    private readonly Dictionary<string, ClientState> _clients = [];
    private readonly Lock _gate = new();

    public TrafficDecision TryStart(string clientKey, int maxPerHour, int maxConcurrent)
    {
        if (maxPerHour <= 0 && maxConcurrent <= 0)
        {
            return new TrafficDecision(TrafficVerdict.Allowed, null, 0);
        }

        var now = DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (_clients.Count >= SweepThreshold) Sweep(now);

            if (!_clients.TryGetValue(clientKey, out var state))
            {
                state = new ClientState();
                _clients[clientKey] = state;
            }

            state.LastSeen = now;

            if (maxConcurrent > 0 && state.Concurrent >= maxConcurrent)
            {
                return new TrafficDecision(TrafficVerdict.TooManyConcurrent, null, 30);
            }

            if (maxPerHour > 0)
            {
                var cutoff = now - Window;
                while (state.Starts.Count > 0 && state.Starts.Peek() < cutoff) state.Starts.Dequeue();

                if (state.Starts.Count >= maxPerHour)
                {
                    // Retry when the oldest start ages out of the window.
                    var retryAt = state.Starts.Peek() + Window;
                    var seconds = (int)Math.Ceiling((retryAt - now).TotalSeconds);

                    return new TrafficDecision(
                        TrafficVerdict.TooManyRequests, null, Math.Clamp(seconds, 1, 3600));
                }

                state.Starts.Enqueue(now);
            }

            state.Concurrent++;

            return new TrafficDecision(TrafficVerdict.Allowed, new Slot(this, clientKey), 0);
        }
    }

    private void Release(string clientKey)
    {
        lock (_gate)
        {
            if (!_clients.TryGetValue(clientKey, out var state)) return;

            if (state.Concurrent > 0) state.Concurrent--;
            state.LastSeen = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>Drops clients with nothing running and no start still inside the window.</summary>
    private void Sweep(DateTimeOffset now)
    {
        var cutoff = now - Window;

        var stale = _clients
            .Where(kv => kv.Value.Concurrent == 0 && kv.Value.LastSeen < cutoff)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in stale) _clients.Remove(key);
    }

    private sealed class ClientState
    {
        public readonly Queue<DateTimeOffset> Starts = new();
        public int Concurrent;
        public DateTimeOffset LastSeen = DateTimeOffset.UtcNow;
    }

    /// <summary>Releases the client's concurrency slot once. Further disposals do nothing.</summary>
    private sealed class Slot(DownloadTrafficGuard guard, string clientKey) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            guard.Release(clientKey);
        }
    }
}
