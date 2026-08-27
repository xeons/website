using Microsoft.EntityFrameworkCore;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Web.Middleware;

/// <summary>
/// Applies the admin-managed redirect table.
///
/// This runs before routing on purpose. The CMS registers a catch-all Blazor route, so by the
/// time routing has run every path has an endpoint and a "did anything match?" check would
/// never fire. The lookup is against an in-memory map, so the cost per request is a dictionary
/// probe rather than a query.
/// </summary>
public class RedirectRulesMiddleware(RequestDelegate next, ILogger<RedirectRulesMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IRedirectMap map, AppDbContext db)
    {
        var path = Normalize(context.Request.Path.Value);

        if (path.Length > 0 && !path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            var rule = await map.FindAsync(path, context.RequestAborted);

            if (rule is not null)
            {
                var target = rule.ToUrl;

                // Carry the query string across so campaign tags survive the hop.
                if (context.Request.QueryString.HasValue && !target.Contains('?'))
                {
                    target += context.Request.QueryString.Value;
                }

                await TrackHitAsync(db, rule.Id);

                context.Response.Redirect(target, rule.StatusCode == 301);
                return;
            }
        }

        await next(context);
    }

    private async Task TrackHitAsync(AppDbContext db, int redirectId)
    {
        try
        {
            await db.Redirects
                .Where(r => r.Id == redirectId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.HitCount, r => r.HitCount + 1)
                    .SetProperty(r => r.LastHitAt, DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            // A stats write must never turn a working redirect into a 500.
            logger.LogDebug(ex, "Could not record a hit for redirect {Id}.", redirectId);
        }
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/") return string.Empty;
        return "/" + path.Trim('/').ToLowerInvariant();
    }
}

public static class RedirectRulesExtensions
{
    public static IApplicationBuilder UseRedirectRules(this IApplicationBuilder app) =>
        app.UseMiddleware<RedirectRulesMiddleware>();
}
