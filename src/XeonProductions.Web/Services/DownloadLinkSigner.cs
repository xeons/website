using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace XeonProductions.Web.Services;

/// <summary>
/// Signs and validates download transfer tokens using the application's data protection key
/// ring, so tokens remain valid across restarts and instances.
/// </summary>
public class DownloadLinkSigner(IDataProtectionProvider provider) : IDownloadLinkSigner
{
    private readonly ITimeLimitedDataProtector _protector =
        provider.CreateProtector("XeonProductions.Downloads.v1").ToTimeLimitedDataProtector();

    public string Issue(int downloadId, string clientKey, TimeSpan lifetime) =>
        _protector.Protect($"{downloadId}|{clientKey}", lifetime);

    public int? Validate(string token, string clientKey)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        string payload;

        try
        {
            payload = _protector.Unprotect(token);
        }
        catch (Exception)
        {
            // The token arrives from the URL, so malformed input is as likely as a bad
            // signature or an expiry. All of them mean the same thing to the caller.
            return null;
        }

        var separator = payload.IndexOf('|');
        if (separator <= 0) return null;

        if (!int.TryParse(payload[..separator], out var id)) return null;

        var expected = Encoding.UTF8.GetBytes(payload[(separator + 1)..]);
        var actual = Encoding.UTF8.GetBytes(clientKey);

        return CryptographicOperations.FixedTimeEquals(expected, actual) ? id : null;
    }

    public string ClientKey(HttpContext http)
    {
        var address = NormalizeAddress(http.Connection.RemoteIpAddress);
        var agent = http.Request.Headers.UserAgent.ToString();

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"{address}\n{agent}"));

        return Convert.ToHexString(digest.AsSpan(0, 16)).ToLowerInvariant();
    }

    public string RateLimitKey(HttpContext http)
    {
        var ip = http.Connection.RemoteIpAddress;
        if (ip is null) return "unknown";

        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();

        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return ip.ToString();
        }

        // One /64 counts as one client.
        var bytes = ip.GetAddressBytes();
        return Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant() + "::/64";
    }

    private static string NormalizeAddress(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        return (ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip).ToString();
    }
}
