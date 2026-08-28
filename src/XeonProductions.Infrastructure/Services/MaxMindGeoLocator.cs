using System.Net;
using System.Net.Sockets;
using MaxMind.GeoIP2;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Country lookups from a MaxMind GeoLite2 database.
///
/// The reader is opened once and is thread safe. A missing or unreadable file is not an
/// error: country is simply unavailable, and the rest of the statistics carry on. The
/// database is not redistributed with the application, so it has to be downloaded and the
/// path configured.
/// </summary>
public sealed class MaxMindGeoLocator : IGeoLocator, IDisposable
{
    private readonly DatabaseReader? _reader;
    private readonly ILogger<MaxMindGeoLocator> _logger;

    public MaxMindGeoLocator(IOptions<StatsOptions> options, ILogger<MaxMindGeoLocator> logger)
    {
        _logger = logger;

        var path = options.Value.GeoDatabasePath;

        if (string.IsNullOrWhiteSpace(path))
        {
            logger.LogInformation(
                "No geolocation database configured; page views will have no country.");
            return;
        }

        if (!File.Exists(path))
        {
            logger.LogWarning(
                "Geolocation database {Path} was not found; page views will have no country.", path);
            return;
        }

        try
        {
            _reader = new DatabaseReader(path);
            logger.LogInformation("Geolocation database loaded from {Path}.", path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not open the geolocation database at {Path}.", path);
        }
    }

    public bool IsAvailable => _reader is not null;

    public string? CountryCode(IPAddress? address)
    {
        if (_reader is null || address is null) return null;

        var ip = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        // Loopback and private ranges are never in the database, and looking them up on every
        // request during local use would be wasted work.
        if (IsPrivate(ip)) return null;

        try
        {
            return _reader.TryCountry(ip, out var response)
                ? response?.Country?.IsoCode
                : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Country lookup failed.");
            return null;
        }
    }

    private static bool IsPrivate(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
                || ip.GetAddressBytes()[0] == 0xfd || ip.GetAddressBytes()[0] == 0xfc;
        }

        var b = ip.GetAddressBytes();

        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254);
    }

    public void Dispose() => _reader?.Dispose();
}
