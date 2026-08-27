namespace XeonProductions.Web.Middleware;

public static class RedirectRulesExtensions
{
    public static IApplicationBuilder UseRedirectRules(this IApplicationBuilder app) =>
        app.UseMiddleware<RedirectRulesMiddleware>();
}
