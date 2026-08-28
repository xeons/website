using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Components;
using XeonProductions.Web.Endpoints;
using XeonProductions.Web.Middleware;
using XeonProductions.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddXeonInfrastructure(builder.Configuration);

// Resolve the media root once, against the content root rather than the working directory:
// a service run by systemd does not necessarily start in the application folder.
builder.Services.PostConfigure<MediaOptions>(options =>
{
    if (!Path.IsPathRooted(options.StorageRoot))
    {
        options.StorageRoot = Path.Combine(builder.Environment.ContentRootPath, options.StorageRoot);
    }
});

builder.Services.PostConfigure<StatsOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.GeoDatabasePath)
        && !Path.IsPathRooted(options.GeoDatabasePath))
    {
        options.GeoDatabasePath =
            Path.Combine(builder.Environment.ContentRootPath, options.GeoDatabasePath);
    }
});

builder.Services.PostConfigure<DownloadOptions>(options =>
{
    if (!Path.IsPathRooted(options.StorageRoot))
    {
        options.StorageRoot = Path.Combine(builder.Environment.ContentRootPath, options.StorageRoot);
    }
});

// Without a stable key ring, every redeploy invalidates auth cookies and antiforgery
// tokens, silently signing everyone out. Keep the keys on a mounted path.
var keysPath = builder.Configuration["DataProtection:KeysPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "keys");
Directory.CreateDirectory(keysPath);

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("XeonProductions");

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequiredLength = 12;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedAccount = false;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/admin/login";
    options.LogoutPath = "/admin/logout";
    options.AccessDeniedPath = "/admin/login";
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;
    options.Cookie.Name = "xeon.auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole(Roles.Administrator));
    options.AddPolicy("CanEditContent", policy =>
        policy.RequireRole(Roles.Administrator, Roles.Editor));
});

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AdminContentService>();
builder.Services.AddScoped<ThemeCssBuilder>();

// The signer is stateless; the traffic guard holds counters shared across all requests.
builder.Services.AddSingleton<IDownloadLinkSigner, DownloadLinkSigner>();

// One queue shared by every request, drained by the background writer.
builder.Services.AddSingleton<StatsRecorder>();
builder.Services.AddSingleton<IStatsRecorder>(sp => sp.GetRequiredService<StatsRecorder>());
builder.Services.AddHostedService<StatsWriter>();
builder.Services.AddSingleton<IDownloadTrafficGuard, DownloadTrafficGuard>();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes
        .Concat(["application/rss+xml", "application/xml", "image/svg+xml"]);
});

// Behind nginx or Caddy the app must trust the proxy headers to build correct absolute URLs.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

builder.Services.AddRateLimiter(options =>
{
    // 429 says what actually happened. The default, 503, claims the whole site is down.
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // No body is written here on purpose: an empty response is what lets the status code
    // pages middleware step in and render a real page.
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };

    // Applies to the contact page endpoint, which serves the form as well as receiving it.
    // Reading is left unlimited: a shared window over GETs meant a handful of visitors
    // looking at the page locked everyone out of it. Submissions are counted per address so
    // one sender cannot spend everybody's allowance.
    options.AddPolicy("contact-form", http =>
        !HttpMethods.IsPost(http.Request.Method)
            ? RateLimitPartition.GetNoLimiter("read")
            : RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0
                }));

    // The beacon is public and unauthenticated. A page reports a handful of times at most,
    // so this only stops a client sending thousands.
    options.AddPolicy("stats-beacon", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    // Partitioned by address, so one caller cannot spend everyone's allowance. The limits on
    // bytes transferred are separate and live in the site settings.
    options.AddPolicy("download-gateway", http =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 120,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error", createScopeForErrors: true);
    app.UseHsts();
}

// Turns a bare status code into a page. Registered before everything that can produce
// one, and skipped for the JSON endpoints, which should answer with JSON rather than be
// re-executed into markup.
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api")
               && !context.Request.Path.StartsWithSegments("/admin/api"),
    branch => branch.UseStatusCodePagesWithReExecute("/error/{0}"));

app.UseResponseCompression();
app.UseSecurityHeaders();

// Behind a TLS-terminating proxy the proxy already redirects, and doing it here as well
// breaks container health checks that talk plain HTTP to the port.
if (app.Configuration.GetValue("Hosting:UseHttpsRedirection", true))
{
    app.UseHttpsRedirection();
}

// Uploaded media lives outside wwwroot so a redeploy never wipes it.
var mediaOptions = app.Services.GetRequiredService<IOptions<MediaOptions>>().Value;
var mediaRoot = mediaOptions.StorageRoot;
Directory.CreateDirectory(mediaRoot);

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaRoot),
    RequestPath = mediaOptions.PublicBasePath,
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
    }
});

// Downloads get a folder and no static file mount. They are served only through the download
// endpoints; do not add UseStaticFiles for this path.
var downloadOptions = app.Services.GetRequiredService<IOptions<DownloadOptions>>().Value;
Directory.CreateDirectory(downloadOptions.StorageRoot);

// Must precede routing: the CMS catch-all route claims every path.
app.UseRedirectRules();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// After authentication so a signed-in admin can be excluded, and around the endpoints so
// the status and content type are known by the time a view is recorded.
app.UseStats();

// Serves wwwroot and the framework's own assets, including blazor.web.js, from the
// build-generated manifest. UseStaticFiles alone cannot: the framework files are not
// physical files under wwwroot, and the manifest is only wired up in Development.
app.MapStaticAssets();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapFeedEndpoints();
app.MapSitemapEndpoints();
app.MapAccountEndpoints();
app.MapMediaEndpoints();
app.MapDownloadEndpoints();
app.MapStatsEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await DbSeeder.SeedAsync(app.Services);
}

app.Run();
