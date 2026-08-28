namespace XeonProductions.Web.Middleware;

public static class StatsExtensions
{
    public static IApplicationBuilder UseStats(this IApplicationBuilder app) =>
        app.UseMiddleware<StatsMiddleware>();
}
