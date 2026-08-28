using XeonProductions.Domain.Enums;

namespace XeonProductions.Infrastructure.Services;

/// <summary>What could be read from a user agent string.</summary>
public record UserAgentInfo(string? Browser, string? OperatingSystem, DeviceType Device)
{
    public static readonly UserAgentInfo Unknown = new(null, null, DeviceType.Unknown);

    public bool IsBot => Device == DeviceType.Bot;
}
