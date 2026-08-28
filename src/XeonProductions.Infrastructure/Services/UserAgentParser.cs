using System.Text.RegularExpressions;
using XeonProductions.Domain.Enums;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Reads a browser, operating system and device kind out of a user agent string.
///
/// User agent strings are deliberately misleading: nearly every browser claims to be Mozilla,
/// Chrome and Safari at once. Order matters throughout, so the more specific claim is tested
/// before the one it impersonates.
/// </summary>
public static partial class UserAgentParser
{
    /// <summary>
    /// Substrings that mark an automated client. Matched case-insensitively against the whole
    /// string, so they catch the many crawlers that follow the same naming habits.
    /// </summary>
    private static readonly string[] BotMarkers =
    [
        "bot", "crawler", "spider", "scraper", "slurp", "curl", "wget", "python-requests",
        "httpclient", "libwww", "java/", "go-http-client", "okhttp", "axios", "node-fetch",
        "headlesschrome", "phantomjs", "puppeteer", "playwright", "lighthouse", "pingdom",
        "uptimerobot", "monitor", "preview", "fetcher", "archiver", "validator", "feedly",
        "postman", "insomnia", "apache-httpclient", "dataprovider", "semrush", "ahrefs"
    ];

    /// <summary>
    /// A missing user agent counts as automated. Every real browser sends one, so a request
    /// without it is a script, and counting those would inflate the visitor figures.
    /// </summary>
    public static UserAgentInfo Parse(string? userAgent)
    {
        if (IsBot(userAgent)) return new UserAgentInfo(null, null, DeviceType.Bot);

        var ua = userAgent!.Trim();

        return new UserAgentInfo(Browser(ua), OperatingSystem(ua), Device(ua));
    }

    public static bool IsBot(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return true;

        foreach (var marker in BotMarkers)
        {
            if (userAgent.Contains(marker, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static string? Browser(string ua)
    {
        // Edge, Opera and Samsung all carry "Chrome"; Chrome carries "Safari". Most specific
        // first, or everything reports as Safari.
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("Edge/", StringComparison.OrdinalIgnoreCase)) return "Edge";

        if (ua.Contains("OPR/", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("Opera", StringComparison.OrdinalIgnoreCase)) return "Opera";

        if (ua.Contains("SamsungBrowser", StringComparison.OrdinalIgnoreCase)) return "Samsung Internet";
        if (ua.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase)) return "Vivaldi";
        if (ua.Contains("Brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
        if (ua.Contains("Firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (ua.Contains("Chrome", StringComparison.OrdinalIgnoreCase)) return "Chrome";

        // Safari is the only one left that says Safari without saying Chrome.
        if (ua.Contains("Safari", StringComparison.OrdinalIgnoreCase)) return "Safari";

        return null;
    }

    private static string? OperatingSystem(string ua)
    {
        // iPadOS reports as Macintosh, so the iPad check has to come first.
        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "iPadOS";
        if (ua.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("iPod", StringComparison.OrdinalIgnoreCase)) return "iOS";

        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase)) return "Android";
        if (ua.Contains("CrOS", StringComparison.Ordinal)) return "ChromeOS";
        if (ua.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";

        if (ua.Contains("Mac OS X", StringComparison.OrdinalIgnoreCase)
            || ua.Contains("Macintosh", StringComparison.OrdinalIgnoreCase)) return "macOS";

        if (ua.Contains("Ubuntu", StringComparison.OrdinalIgnoreCase)) return "Ubuntu";
        if (ua.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return "Linux";
        if (ua.Contains("FreeBSD", StringComparison.OrdinalIgnoreCase)) return "FreeBSD";

        return null;
    }

    private static DeviceType Device(string ua)
    {
        if (ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return DeviceType.Tablet;

        // Android tablets omit "Mobile"; Android phones include it.
        if (ua.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return ua.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
                ? DeviceType.Mobile
                : DeviceType.Tablet;
        }

        if (TabletPattern().IsMatch(ua)) return DeviceType.Tablet;
        if (MobilePattern().IsMatch(ua)) return DeviceType.Mobile;

        return DeviceType.Desktop;
    }

    [GeneratedRegex("tablet|playbook|silk|kindle", RegexOptions.IgnoreCase)]
    private static partial Regex TabletPattern();

    [GeneratedRegex("mobile|iphone|ipod|phone|blackberry|windows ce|opera mini|iemobile",
        RegexOptions.IgnoreCase)]
    private static partial Regex MobilePattern();
}
