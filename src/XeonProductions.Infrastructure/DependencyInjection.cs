using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddXeonInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        var connectionString = config.GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Connection string 'Default' is missing. Set ConnectionStrings__Default.");

        // Interactive Blazor components outlive a single request, and two renders can overlap
        // on one circuit. They take a factory and own a short-lived context each; server-side
        // rendering and Identity keep the familiar scoped instance, created from that factory.
        services.AddDbContextFactory<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                // A VPS database can blip during a restart; retry rather than 500.
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(5), null);
            });
        });

        services.AddScoped<AppDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.AddMemoryCache();

        services.Configure<MediaOptions>(config.GetSection("Media"));
        services.Configure<DownloadOptions>(config.GetSection("Downloads"));
        services.Configure<SmtpOptions>(config.GetSection("Smtp"));

        services.AddScoped<ISiteSettingsService, SiteSettingsService>();
        services.AddScoped<IContentService, ContentService>();
        services.AddScoped<INavigationService, NavigationService>();
        services.AddScoped<IMediaService, MediaService>();
        services.AddScoped<IDownloadService, DownloadService>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IFeedReader, FeedReader>();

        // Named client for outbound feed fetches, kept away from the default pipeline.
        services.AddHttpClient("feeds", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("XeonProductions/1.0 (+feed reader)");
            client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
        });

        // Holds a cached map and creates its own scope, so it is safe before routing.
        services.AddSingleton<IRedirectMap, RedirectMap>();

        // Stateless and thread-safe; the sanitiser setup is not worth repeating per request.
        services.AddSingleton<IHtmlService, HtmlService>();
        services.AddSingleton<ICodeHighlighter, CodeHighlighter>();
        services.AddSingleton<IContentRenderer, ContentRenderer>();
        services.AddSingleton<IWordPressMarkupCleaner, WordPressMarkupCleaner>();
        services.AddSingleton<IImportLinkRewriter, ImportLinkRewriter>();

        return services;
    }
}
