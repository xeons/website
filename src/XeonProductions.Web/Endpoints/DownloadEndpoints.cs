using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Services;

namespace XeonProductions.Web.Endpoints;

/// <summary>
/// The public download routes and the admin upload.
///
/// <c>/download/{slug}</c> is the stable public address. It returns no file bytes: it checks
/// the request and redirects to a signed transfer link.
///
/// <c>/download/file/{token}</c> serves the file. The token expires and is bound to the
/// client it was issued to.
/// </summary>
public static class DownloadEndpoints
{
    private const string TransferContentType = "application/octet-stream";

    public static IEndpointRouteBuilder MapDownloadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/download/{slug}", GatewayAsync)
            .RequireRateLimiting("download-gateway")
            .AllowAnonymous()
            .WithName("DownloadGateway");

        app.MapMethods("/download/file/{token}", ["GET", "HEAD"], TransferAsync)
            .AllowAnonymous()
            .WithName("DownloadTransfer");

        var admin = app.MapGroup("/admin/api/downloads")
            .RequireAuthorization("CanEditContent");

        admin.MapGet("/token", (HttpContext http, IAntiforgery antiforgery) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return TypedResults.Ok(new { token = antiforgery.GetAndStoreTokens(http).RequestToken });
        });

        // Antiforgery is checked inside the handler. The middleware would fall back to
        // reading the token from the form body, buffering the whole upload to do so.
        admin.MapPost("/upload", UploadAsync)
            .DisableAntiforgery()
            .WithName("DownloadUpload");

        return app;
    }

    private static async Task<IResult> GatewayAsync(
        string slug,
        HttpContext http,
        AppDbContext db,
        ISiteSettingsService settingsService,
        IDownloadLinkSigner signer,
        IServiceScopeFactory scopeFactory,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);

        NoStore(http);

        if (!settings.DownloadsEnabled) return NotFoundPage(settings.SiteTitle);

        var item = await db.Downloads.AsNoTracking()
            .FirstOrDefaultAsync(d => d.Slug == slug, ct);

        if (item is null || !item.IsPublished || !item.HasFile)
        {
            return NotFoundPage(settings.SiteTitle);
        }

        if (item.RequiresAuthentication && http.User.Identity?.IsAuthenticated != true)
        {
            var returnUrl = Uri.EscapeDataString(http.Request.Path + http.Request.QueryString);
            return TypedResults.LocalRedirect($"/admin/login?returnUrl={returnUrl}");
        }

        var protection = item.ProtectionOverride ?? settings.DownloadProtection;

        // Site hosts and partner hosts are kept apart: a cross-site request may be excused by
        // a partner host but never by one of ours.
        var siteHosts = HotlinkPolicy.ParseHosts(settings.SiteUrl);

        var partnerHosts = HotlinkPolicy.ParseHosts(settings.DownloadAllowedReferrers)
            .Concat(HotlinkPolicy.ParseHosts(item.AllowedReferrers));

        var origin = HotlinkPolicy.Classify(http.Request, siteHosts, partnerHosts);

        if (!HotlinkPolicy.IsAllowed(origin, protection))
        {
            var logger = loggerFactory.CreateLogger("Downloads");

            logger.LogInformation(
                "Blocked a {Origin} request for {Slug} from {Address}, referrer {Referer}.",
                origin, slug, http.Connection.RemoteIpAddress, http.Request.Headers.Referer.ToString());

            // Not awaited, and not on the request's token, so it still lands if the client
            // disconnects.
            _ = CountAsync(scopeFactory, item.Id, blocked: true);

            return BlockedPage(settings.SiteTitle, origin);
        }

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(settings.DownloadLinkLifetimeMinutes, 1, 1440));
        var token = signer.Issue(item.Id, signer.ClientKey(http), lifetime);

        return TypedResults.Redirect($"/download/file/{token}");
    }

    private static async Task<IResult> TransferAsync(
        string token,
        HttpContext http,
        AppDbContext db,
        ISiteSettingsService settingsService,
        IDownloadService downloads,
        IDownloadLinkSigner signer,
        IDownloadTrafficGuard guard,
        IServiceScopeFactory scopeFactory,
        CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);

        NoStore(http);

        if (!settings.DownloadsEnabled) return NotFoundPage(settings.SiteTitle);

        var id = signer.Validate(token, signer.ClientKey(http));

        if (id is null) return ExpiredPage(settings.SiteTitle);

        var item = await db.Downloads.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct);

        if (item is null || !item.IsPublished || !item.HasFile)
        {
            return NotFoundPage(settings.SiteTitle);
        }

        // Re-checked rather than taken from the token, which may predate a sign-out.
        if (item.RequiresAuthentication && http.User.Identity?.IsAuthenticated != true)
        {
            return ExpiredPage(settings.SiteTitle);
        }

        var file = await downloads.OpenAsync(item, ct);
        if (file is null) return NotFoundPage(settings.SiteTitle);

        var fileName = string.IsNullOrWhiteSpace(item.FileName) ? item.Slug : item.FileName;

        // A HEAD transfers nothing, so it takes no slot and no rate limit allowance.
        if (HttpMethods.IsHead(http.Request.Method))
        {
            http.Response.ContentType = TransferContentType;
            http.Response.ContentLength = file.Length;
            http.Response.Headers.AcceptRanges = "bytes";
            http.Response.Headers.ContentDisposition = AttachmentHeader(fileName);

            return TypedResults.Empty;
        }

        var decision = guard.TryStart(
            signer.RateLimitKey(http),
            settings.DownloadsPerIpPerHour,
            settings.MaxConcurrentDownloadsPerIp);

        if (!decision.Allowed)
        {
            http.Response.Headers.RetryAfter = decision.RetryAfterSeconds.ToString();
            return BusyPage(settings.SiteTitle, decision.Verdict);
        }

        if (IsFreshStart(http.Request))
        {
            _ = CountAsync(scopeFactory, item.Id, blocked: false);
        }

        // Parsed before the slot is committed to a stream, so a failure here cannot strand it.
        var entityTag = EntityTagHeaderValue.Parse(file.EntityTag);

        var source = new FileStream(
            file.AbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);

        var bytesPerSecond = Math.Max(0, settings.DownloadThrottleKbps) * 1024L;

        // The stream owns the slot from here; the framework disposes it however the response
        // ends, including a client that aborts.
        var stream = new DownloadTransferStream(source, decision.Slot, bytesPerSecond);

        http.Response.Headers.ContentDisposition = AttachmentHeader(fileName);

        // Always octet-stream, so a stored file cannot be rendered inside this origin.
        return TypedResults.File(
            stream,
            contentType: TransferContentType,
            lastModified: file.LastModified,
            entityTag: entityTag,
            enableRangeProcessing: true);
    }

    private static async Task<IResult> UploadAsync(
        HttpContext http,
        IAntiforgery antiforgery,
        IDownloadService downloads,
        ILoggerFactory loggerFactory,
        [Microsoft.AspNetCore.Mvc.FromQuery] int? id,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("DownloadUpload");

        // Checked before the body is touched, so validation never reads the form.
        if (string.IsNullOrEmpty(http.Request.Headers["RequestVerificationToken"].ToString()))
        {
            return TypedResults.BadRequest(new { message = "The request verification token is missing." });
        }

        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return TypedResults.BadRequest(
                new { message = "This page has gone stale. Reload it and try the upload again." });
        }

        if (!http.Request.HasFormContentType ||
            !http.Request.ContentType!.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            return TypedResults.BadRequest(new { message = "Expected a multipart upload." });
        }

        var boundary = http.Request.GetMultipartBoundary();
        if (string.IsNullOrEmpty(boundary))
        {
            return TypedResults.BadRequest(new { message = "The upload was malformed." });
        }

        // Lifts Kestrel's 30 MB body cap. DownloadOptions.MaxFileSizeBytes is the real limit.
        var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = null;

        var userId = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var reader = new MultipartReader(boundary, http.Request.Body);

        string? title = null;

        MultipartSection? section;
        while ((section = await reader.ReadNextSectionAsync(ct)) is not null)
        {
            if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
            {
                continue;
            }

            // The body is read once, forwards, so the title must arrive before the file part.
            if (disposition.IsFormDisposition()
                && disposition.Name.Value == "title"
                && title is null)
            {
                title = (await section.ReadAsStringAsync(ct)).Trim();
                continue;
            }

            if (!disposition.IsFileDisposition()) continue;

            var fileName = disposition.FileNameStar.Value ?? disposition.FileName.Value ?? "download";
            var contentType = section.ContentType ?? "application/octet-stream";

            var result = id is int existing
                ? await downloads.ReplaceFileAsync(existing, section.Body, fileName, contentType, ct)
                : await downloads.CreateAsync(section.Body, fileName, contentType, title, userId, ct);

            if (!result.Success || result.Item is null)
            {
                logger.LogWarning("Download upload of {Name} was rejected: {Error}", fileName, result.Error);
                return TypedResults.BadRequest(new { message = result.Error ?? "The upload failed." });
            }

            return TypedResults.Ok(new
            {
                id = result.Item.Id,
                slug = result.Item.Slug,
                url = downloads.PublicUrl(result.Item),
                size = result.Item.SizeBytes
            });
        }

        return TypedResults.BadRequest(new { message = "No file was received." });
    }

    /// <summary>True for a request with no Range, or one starting at the first byte.</summary>
    private static bool IsFreshStart(HttpRequest request)
    {
        var range = request.Headers.Range.ToString();
        if (string.IsNullOrEmpty(range)) return true;

        return range.Replace(" ", string.Empty).StartsWith("bytes=0-", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Records a hit in its own scope. The task outlives the response, so the request's
    /// service provider cannot be used.
    /// </summary>
    private static async Task CountAsync(IServiceScopeFactory scopeFactory, int downloadId, bool blocked)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var downloads = scope.ServiceProvider.GetRequiredService<IDownloadService>();

            await downloads.CountHitAsync(downloadId, blocked, CancellationToken.None);
        }
        catch
        {
            // CountHitAsync logs its own failures. This guards service resolution only.
        }
    }

    /// <summary>Builds an RFC 6266 attachment header, encoding the name for both forms.</summary>
    private static string AttachmentHeader(string fileName)
    {
        var header = new ContentDispositionHeaderValue("attachment");
        header.SetHttpFileName(fileName);

        return header.ToString();
    }

    /// <summary>Marks the response uncacheable by any shared cache and unindexable.</summary>
    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "private, no-store, max-age=0";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    private static IResult NotFoundPage(string siteTitle) => Page(
        404, siteTitle, "That download is not here",
        "The file may have been withdrawn, or the link may have been mistyped.");

    private static IResult ExpiredPage(string siteTitle) => Page(
        403, siteTitle, "This link has expired",
        "Download links are issued for one visitor and a short window, so a copied or "
        + "shared link stops working. Open the page the file was offered on and follow the "
        + "link from there.");

    private static IResult BlockedPage(string siteTitle, RequestOrigin origin)
    {
        var detail = origin == RequestOrigin.Foreign
            ? "This file was linked from another site. Files here are served to visitors of "
              + "this site rather than embedded elsewhere."
            : "This file has to be reached by following a link on the site, and this request "
              + "arrived without one.";

        return Page(403, siteTitle, "This download cannot be served here", detail);
    }

    private static IResult BusyPage(string siteTitle, TrafficVerdict verdict) => Page(
        429, siteTitle,
        verdict == TrafficVerdict.TooManyConcurrent ? "Too many downloads at once" : "Slow down",
        verdict == TrafficVerdict.TooManyConcurrent
            ? "There are already several transfers running from your connection. Let one "
              + "finish and try this one again."
            : "Rather a lot of files have been requested from your connection in the last "
              + "hour. Try again a little later.");

    /// <summary>
    /// A self-contained response page for a refused download. Links the site stylesheet and
    /// uses the theme's class names, since an endpoint has no layout to render into.
    /// </summary>
    private static IResult Page(int status, string siteTitle, string heading, string message)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);

        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8" />
            <meta name="viewport" content="width=device-width, initial-scale=1" />
            <meta name="robots" content="noindex,nofollow" />
            <title>{E(heading)} - {E(siteTitle)}</title>
            <link rel="stylesheet" href="/app.css" />
            </head>
            <body>
            <div class="site">
              <div id="content" class="site-content no-sidebar">
                <main class="content-area">
                  <article class="entry">
                    <h1 class="entry-title">{E(heading)}</h1>
                    <div class="entry-content">
                      <p>{E(message)}</p>
                      <p><a href="/">Back to {E(siteTitle)}</a></p>
                    </div>
                  </article>
                </main>
              </div>
            </div>
            </body>
            </html>
            """;

        return TypedResults.Text(html, "text/html; charset=utf-8", statusCode: status);
    }
}
