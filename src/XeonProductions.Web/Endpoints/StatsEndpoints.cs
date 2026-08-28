using Microsoft.Extensions.Options;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Services;

namespace XeonProductions.Web.Endpoints;

/// <summary>
/// The dwell time beacon. The middleware records that a page was served; only the browser
/// knows how long it stayed open.
/// </summary>
public static class StatsEndpoints
{
    public static IEndpointRouteBuilder MapStatsEndpoints(this IEndpointRouteBuilder app)
    {
        // sendBeacon always posts, and cannot set headers or carry an antiforgery token.
        // Nothing here is authenticated and the only effect is a dwell time on a row whose
        // id the caller must already know, so there is no action to forge.
        app.MapPost("/api/stats/ping", PingAsync)
            .AllowAnonymous()
            .DisableAntiforgery()
            .RequireRateLimiting("stats-beacon")
            .WithName("StatsBeacon");

        return app;
    }

    private static IResult PingAsync(
        HttpContext http,
        IStatsRecorder recorder,
        IOptions<StatsOptions> options)
    {
        var opts = options.Value;

        if (!opts.Enabled) return TypedResults.NoContent();

        var query = http.Request.Query;

        if (!Guid.TryParse(query["v"], out var viewId)) return TypedResults.NoContent();
        if (!int.TryParse(query["s"], out var seconds)) return TypedResults.NoContent();

        // A tab left open for a week is not a week of reading, and a negative figure is a
        // client with a broken clock.
        seconds = Math.Clamp(seconds, 0, Math.Max(1, opts.MaxDurationSeconds));

        if (seconds > 0) recorder.RecordDuration(viewId, seconds);

        // 204 keeps the response empty; nothing is waiting on it.
        return TypedResults.NoContent();
    }
}
