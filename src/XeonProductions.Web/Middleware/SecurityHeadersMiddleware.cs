using System.Security.Cryptography;

namespace XeonProductions.Web.Middleware;

public static class SecurityHeaders
{
    /// <summary>Key under which the per-request CSP nonce is published to the components.</summary>
    public const string NonceItemKey = "csp-nonce";

    public static string? GetCspNonce(this HttpContext? context) =>
        context?.Items.TryGetValue(NonceItemKey, out var value) == true ? value as string : null;

    /// <summary>
    /// Baseline hardening. Inline scripts are allowed only when they carry the request nonce,
    /// which keeps the theme bootstrap working without opening the page to injected script.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[NonceItemKey] = nonce;

            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["X-Frame-Options"] = "SAMEORIGIN";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";

            headers["Content-Security-Policy"] =
                "default-src 'self'; " +
                $"script-src 'self' 'nonce-{nonce}'; " +
                "img-src 'self' data: blob: https:; " +
                "media-src 'self' https:; " +
                // The theme writes its palette into a style block, and Blazor sets inline styles.
                "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
                "font-src 'self' data: https://fonts.gstatic.com; " +
                // Blazor's server runtime needs a websocket back to the origin.
                "connect-src 'self' ws: wss:; " +
                // 'self' and blob: are for the editor, which renders its editable area in
                // an iframe; the rest are the embed hosts the sanitiser permits in content.
                "frame-src 'self' blob: https://www.youtube.com https://www.youtube-nocookie.com " +
                    "https://player.vimeo.com https://codepen.io https://open.spotify.com; " +
                "frame-ancestors 'self'; " +
                "base-uri 'self'; " +
                "object-src 'none'; " +
                "form-action 'self'";

            await next();
        });
}
