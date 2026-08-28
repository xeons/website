using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Services;

namespace XeonProductions.Web.Middleware;

/// <summary>
/// Records a page view for each HTML page served.
///
/// Capture is server side so it does not depend on script running, is unaffected by blockers,
/// and counts visitors whose browsers would never have reported themselves. The one thing it
/// cannot see is how long the page stayed open, which is what the beacon adds.
///
/// The view id is put on the context before the response starts so the page can render it,
/// and the row itself is queued rather than written, keeping the database off the hot path.
/// </summary>
public class StatsMiddleware(RequestDelegate next, IOptions<StatsOptions> options)
{
    /// <summary>Key under which the page reads the id to hand to the beacon.</summary>
    public const string ViewIdItemKey = "stats-view-id";

    private readonly StatsOptions _opts = options.Value;

    public async Task InvokeAsync(
        HttpContext context,
        IStatsRecorder recorder,
        IVisitorHasher hasher,
        IGeoLocator geo,
        IMemoryCache cache)
    {
        if (!ShouldConsider(context))
        {
            await next(context);
            return;
        }

        var viewId = Guid.NewGuid();
        context.Items[ViewIdItemKey] = viewId;

        // Read before the pipeline runs: a component may otherwise have changed the path,
        // and the connection details are gone once the response completes.
        var path = context.Request.Path.Value ?? "/";
        var referer = context.Request.Headers.Referer.ToString();
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var address = context.Connection.RemoteIpAddress;

        var captured = false;

        context.Response.OnStarting(() =>
        {
            // Only now is the status and content type known, and a redirect or a 404 is not
            // a page view.
            captured = context.Response.StatusCode == StatusCodes.Status200OK
                       && (context.Response.ContentType?.Contains("text/html",
                           StringComparison.OrdinalIgnoreCase) ?? false);

            return Task.CompletedTask;
        });

        await next(context);

        if (!captured) return;

        var agent = UserAgentParser.Parse(userAgent);
        if (agent.IsBot) return;

        var now = DateTimeOffset.UtcNow;
        var visitor = await hasher.VisitorAsync(address, userAgent, now, context.RequestAborted);

        var window = TimeSpan.FromMinutes(Math.Max(1, _opts.SessionWindowMinutes));
        var session = hasher.Session(visitor, now, window);

        // First view of a session marks the entry page. Held in memory rather than queried,
        // because a lookup per page request is exactly what this design avoids. A restart
        // loses the set, which at worst counts one extra entry page per live session.
        var sessionKey = $"stats-session:{session}";
        var isEntry = !cache.TryGetValue(sessionKey, out _);

        cache.Set(sessionKey, true, new MemoryCacheEntryOptions { SlidingExpiration = window });

        recorder.Record(new PageView
        {
            ViewId = viewId,
            Path = Truncate(path, 500) ?? "/",
            ViewedAt = now,
            SessionId = session,
            VisitorHash = visitor,
            ReferrerHost = RefererHost(referer),
            ReferrerUrl = Truncate(NullIfEmpty(referer), 1000),
            CountryCode = geo.CountryCode(address),
            Browser = agent.Browser,
            OperatingSystem = agent.OperatingSystem,
            Device = agent.Device,
            IsEntry = isEntry
        });
    }

    private bool ShouldConsider(HttpContext context)
    {
        if (!_opts.Enabled) return false;
        if (!HttpMethods.IsGet(context.Request.Method)) return false;

        if (_opts.IgnoreAuthenticated && context.User.Identity?.IsAuthenticated == true)
        {
            return false;
        }

        var path = context.Request.Path.Value ?? "/";

        foreach (var prefix in _opts.IgnoredPathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        }

        // Anything with an extension is an asset rather than a page.
        var last = path.AsSpan()[(path.LastIndexOf('/') + 1)..];
        if (last.Contains('.')) return false;

        return true;
    }

    /// <summary>Null for a direct visit or a referrer from this same site.</summary>
    private static string? RefererHost(string referer)
    {
        if (string.IsNullOrWhiteSpace(referer)) return null;

        return Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            ? uri.Host.ToLowerInvariant()
            : null;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>Keeps a value inside its column width. Referrer URLs can be very long.</summary>
    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max];
}
