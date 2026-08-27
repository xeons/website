using XeonProductions.Domain.Enums;

namespace XeonProductions.Web.Services;

/// <summary>
/// Classifies where a download request came from, using <c>Sec-Fetch-Site</c> where the
/// client sends it and falling back to <c>Referer</c>.
/// </summary>
public static class HotlinkPolicy
{
    /// <summary>
    /// Fills in for a referrer that was sent but could not be parsed. Contains a space, so it
    /// matches no real host and resolves to <see cref="RequestOrigin.Foreign"/>.
    /// </summary>
    private const string UnreadableHost = " unreadable";

    /// <param name="siteHosts">
    /// This site's own hostnames. The host the request arrived on is added automatically.
    /// </param>
    /// <param name="partnerHosts">
    /// Third-party hosts permitted to link directly at a file.
    /// </param>
    public static RequestOrigin Classify(
        HttpRequest request, IEnumerable<string> siteHosts, IEnumerable<string> partnerHosts)
    {
        var own = Normalize(siteHosts);
        var partners = Normalize(partnerHosts);

        if (request.Host.HasValue) own.Add(request.Host.Host.ToLowerInvariant());

        var fetchSite = request.Headers["Sec-Fetch-Site"].ToString();

        if (!string.IsNullOrEmpty(fetchSite))
        {
            switch (fetchSite.Trim().ToLowerInvariant())
            {
                case "same-origin":
                case "same-site":
                    return RequestOrigin.Allowed;

                case "none":
                    return RequestOrigin.Unknown;

                case "cross-site":
                    // Only the partner list applies here, not this site's own hosts. A
                    // cross-site request naming one of our hosts as its referrer is a
                    // contradiction, and is treated as foreign.
                    return RefererHost(request) is string host && Matches(host, partners)
                        ? RequestOrigin.Allowed
                        : RequestOrigin.Foreign;
            }
        }

        var referer = RefererHost(request);

        if (referer is null) return RequestOrigin.Unknown;

        return Matches(referer, own) || Matches(referer, partners)
            ? RequestOrigin.Allowed
            : RequestOrigin.Foreign;
    }

    public static bool IsAllowed(RequestOrigin origin, HotlinkProtection protection) => protection switch
    {
        HotlinkProtection.Off => true,
        HotlinkProtection.Lenient => origin != RequestOrigin.Foreign,
        HotlinkProtection.Strict => origin == RequestOrigin.Allowed,
        _ => true
    };

    /// <summary>
    /// Splits a comma, semicolon, space or newline separated host list. Accepts a full URL
    /// as readily as a bare host, and drops any port.
    /// </summary>
    public static IEnumerable<string> ParseHosts(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) yield break;

        foreach (var part in value.Split([',', ';', '\n', '\r', ' '],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var host = part;

            // The scheme is required before parsing as a URI. Without it "example.com:8080"
            // parses as scheme "example.com" with path "8080", leaving an empty host.
            if (host.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate(host, UriKind.Absolute, out var uri))
            {
                host = uri.Host;
            }

            host = host.Trim('/').ToLowerInvariant();

            var colon = host.LastIndexOf(':');
            if (colon > 0 && !host.Contains(']')) host = host[..colon];

            if (host.Length > 0) yield return host;
        }
    }

    private static HashSet<string> Normalize(IEnumerable<string> hosts) =>
        new(hosts.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0),
            StringComparer.Ordinal);

    /// <summary>An exact match, or any subdomain of an allowed host.</summary>
    private static bool Matches(string host, HashSet<string> allowed) =>
        allowed.Contains(host) || allowed.Any(a => host.EndsWith("." + a, StringComparison.Ordinal));

    /// <summary>The referrer host, null when none was sent.</summary>
    private static string? RefererHost(HttpRequest request)
    {
        var referer = request.Headers.Referer.ToString();
        if (string.IsNullOrWhiteSpace(referer)) return null;

        return Uri.TryCreate(referer, UriKind.Absolute, out var uri)
            ? uri.Host.ToLowerInvariant()
            : UnreadableHost;
    }
}
