namespace XeonProductions.Web.Services;

public interface IDownloadLinkSigner
{
    /// <summary>Issues a transfer token for one download, bound to one client and window.</summary>
    string Issue(int downloadId, string clientKey, TimeSpan lifetime);

    /// <summary>
    /// The download id, or null when the token is expired, altered, or was issued to a
    /// different client.
    /// </summary>
    int? Validate(string token, string clientKey);

    /// <summary>An identifier for the requesting client, derived from address and user agent.</summary>
    string ClientKey(HttpContext http);

    /// <summary>The key a rate limit is counted against. IPv6 is grouped to its /64.</summary>
    string RateLimitKey(HttpContext http);
}
