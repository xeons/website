using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
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
    options.AddFixedWindowLimiter("contact-form", limiter =>
    {
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromMinutes(10);
        limiter.QueueLimit = 0;
    });
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

// Must precede routing: the CMS catch-all route claims every path.
app.UseRedirectRules();

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// Serves wwwroot and the framework's own assets, including blazor.web.js, from the
// build-generated manifest. UseStaticFiles alone cannot: the framework files are not
// physical files under wwwroot, and the manifest is only wired up in Development.
app.MapStaticAssets();

app.MapHealthChecks("/health").AllowAnonymous();

app.MapFeedEndpoints();
app.MapSitemapEndpoints();
app.MapAccountEndpoints();
app.MapMediaEndpoints();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    await DbSeeder.SeedAsync(app.Services);
}

app.Run();
