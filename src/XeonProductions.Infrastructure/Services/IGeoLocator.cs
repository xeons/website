using System.Net;

namespace XeonProductions.Infrastructure.Services;

public interface IGeoLocator
{
    /// <summary>
    /// ISO 3166-1 alpha-2 for the address, or null when the address is unknown, private, or
    /// no database is configured.
    /// </summary>
    string? CountryCode(IPAddress? address);

    /// <summary>False when no database was loaded, so the admin can say why country is empty.</summary>
    bool IsAvailable { get; }
}
