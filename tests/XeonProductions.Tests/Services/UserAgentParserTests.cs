using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class UserAgentParserTests
{
    private const string Chrome =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/122.0.0.0 Safari/537.36";

    private const string Edge =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/122.0.0.0 Safari/537.36 Edg/122.0.0.0";

    private const string SafariMac =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) "
        + "Version/17.3 Safari/605.1.15";

    private const string SafariIPhone =
        "Mozilla/5.0 (iPhone; CPU iPhone OS 17_3 like Mac OS X) AppleWebKit/605.1.15 "
        + "(KHTML, like Gecko) Version/17.3 Mobile/15E148 Safari/604.1";

    private const string IPad =
        "Mozilla/5.0 (iPad; CPU OS 17_3 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) "
        + "Version/17.3 Mobile/15E148 Safari/604.1";

    private const string AndroidPhone =
        "Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/122.0.0.0 Mobile Safari/537.36";

    private const string AndroidTablet =
        "Mozilla/5.0 (Linux; Android 13; SM-X710) AppleWebKit/537.36 (KHTML, like Gecko) "
        + "Chrome/122.0.0.0 Safari/537.36";

    private const string FirefoxLinux =
        "Mozilla/5.0 (X11; Linux x86_64; rv:123.0) Gecko/20100101 Firefox/123.0";

    /// <summary>
    /// Nearly every browser claims to be several others, so the specific claim has to win.
    /// </summary>
    [Theory]
    [InlineData(Chrome, "Chrome")]
    [InlineData(Edge, "Edge")]
    [InlineData(SafariMac, "Safari")]
    [InlineData(SafariIPhone, "Safari")]
    [InlineData(FirefoxLinux, "Firefox")]
    [InlineData(AndroidPhone, "Chrome")]
    public void TheBrowserIsReadPastTheImpersonation(string ua, string expected) =>
        Assert.Equal(expected, UserAgentParser.Parse(ua).Browser);

    [Theory]
    [InlineData(Chrome, "Windows")]
    [InlineData(Edge, "Windows")]
    [InlineData(SafariMac, "macOS")]
    [InlineData(SafariIPhone, "iOS")]
    [InlineData(IPad, "iPadOS")]
    [InlineData(AndroidPhone, "Android")]
    [InlineData(FirefoxLinux, "Linux")]
    public void TheOperatingSystemIsRead(string ua, string expected) =>
        Assert.Equal(expected, UserAgentParser.Parse(ua).OperatingSystem);

    /// <summary>An iPad reports itself as a Macintosh, so it must be checked before macOS.</summary>
    [Fact]
    public void AnIPadIsNotMistakenForAMac()
    {
        var info = UserAgentParser.Parse(IPad);

        Assert.Equal("iPadOS", info.OperatingSystem);
        Assert.Equal(DeviceType.Tablet, info.Device);
    }

    /// <summary>Android tablets are told from phones only by the absence of "Mobile".</summary>
    [Theory]
    [InlineData(AndroidPhone, DeviceType.Mobile)]
    [InlineData(AndroidTablet, DeviceType.Tablet)]
    [InlineData(SafariIPhone, DeviceType.Mobile)]
    [InlineData(IPad, DeviceType.Tablet)]
    [InlineData(Chrome, DeviceType.Desktop)]
    [InlineData(SafariMac, DeviceType.Desktop)]
    [InlineData(FirefoxLinux, DeviceType.Desktop)]
    public void TheDeviceKindIsRead(string ua, DeviceType expected) =>
        Assert.Equal(expected, UserAgentParser.Parse(ua).Device);

    [Theory]
    [InlineData("Googlebot/2.1 (+http://www.google.com/bot.html)")]
    [InlineData("Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)")]
    [InlineData("Mozilla/5.0 (compatible; AhrefsBot/7.0; +http://ahrefs.com/robot/)")]
    [InlineData("curl/8.4.0")]
    [InlineData("Wget/1.21.3")]
    [InlineData("python-requests/2.31.0")]
    [InlineData("Go-http-client/2.0")]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) HeadlessChrome/122.0.0.0")]
    [InlineData("UptimeRobot/2.0")]
    public void AutomatedClientsAreFlagged(string ua)
    {
        var info = UserAgentParser.Parse(ua);

        Assert.True(info.IsBot);
        Assert.Equal(DeviceType.Bot, info.Device);
    }

    [Theory]
    [InlineData(Chrome)]
    [InlineData(SafariIPhone)]
    [InlineData(FirefoxLinux)]
    public void RealBrowsersAreNotFlagged(string ua) =>
        Assert.False(UserAgentParser.Parse(ua).IsBot);

    /// <summary>
    /// A client that sends nothing is far more likely to be a script than a browser, and
    /// counting it as a visitor would inflate the figures.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingUserAgentIsTreatedAsAutomated(string? ua)
    {
        var info = UserAgentParser.Parse(ua);

        Assert.True(info.IsBot);
        Assert.Equal(DeviceType.Bot, info.Device);
    }

    [Fact]
    public void AnUnrecognisedBrowserYieldsNullRatherThanAGuess()
    {
        var info = UserAgentParser.Parse("SomeEntirelyNewClient/1.0");

        Assert.Null(info.Browser);
        Assert.Null(info.OperatingSystem);
    }
}
