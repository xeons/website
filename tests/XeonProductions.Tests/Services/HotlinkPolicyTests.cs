using Microsoft.AspNetCore.Http;
using XeonProductions.Domain.Enums;
using XeonProductions.Web.Services;

namespace XeonProductions.Tests.Services;

public class HotlinkPolicyTests
{
    private const string SiteHost = "xeonproductions.com";
    private const string Partner = "partner.example.com";

    private static readonly string[] Site = [SiteHost];
    private static readonly string[] Partners = [Partner];
    private static readonly string[] None = [];

    private static HttpRequest Request(string? referer = null, string? fetchSite = null,
        string host = SiteHost)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString(host);

        if (referer is not null) context.Request.Headers.Referer = referer;
        if (fetchSite is not null) context.Request.Headers["Sec-Fetch-Site"] = fetchSite;

        return context.Request;
    }

    // --- Sec-Fetch-Site, which is preferred when the client sends it ---

    [Theory]
    [InlineData("same-origin")]
    [InlineData("same-site")]
    [InlineData("Same-Origin")]
    public void SameSiteFetchIsAllowed(string fetchSite) =>
        Assert.Equal(
            RequestOrigin.Allowed,
            HotlinkPolicy.Classify(Request(fetchSite: fetchSite), Site, None));

    [Fact]
    public void DirectNavigationIsUnknownRatherThanForeign() =>
        Assert.Equal(
            RequestOrigin.Unknown,
            HotlinkPolicy.Classify(Request(fetchSite: "none"), Site, None));

    [Fact]
    public void CrossSiteFetchIsForeign() =>
        Assert.Equal(
            RequestOrigin.Foreign,
            HotlinkPolicy.Classify(Request(fetchSite: "cross-site"), Site, None));

    /// <summary>
    /// A browser reporting cross-site cannot also be on one of our own pages, so a referrer
    /// claiming otherwise is a forgery and must not earn the partner exemption.
    /// </summary>
    [Fact]
    public void CrossSiteFetchClaimingOurOwnRefererIsStillForeign() =>
        Assert.Equal(
            RequestOrigin.Foreign,
            HotlinkPolicy.Classify(
                Request(referer: $"https://{SiteHost}/some/post", fetchSite: "cross-site"),
                Site, Partners));

    [Fact]
    public void CrossSiteFetchFromANamedPartnerIsAllowed() =>
        Assert.Equal(
            RequestOrigin.Allowed,
            HotlinkPolicy.Classify(
                Request(referer: $"https://{Partner}/page", fetchSite: "cross-site"),
                Site, Partners));

    [Fact]
    public void CrossSiteFetchFromAPartnerSubdomainIsAllowed() =>
        Assert.Equal(
            RequestOrigin.Allowed,
            HotlinkPolicy.Classify(
                Request(referer: $"https://cdn.{Partner}/page", fetchSite: "cross-site"),
                Site, Partners));

    [Fact]
    public void ALookalikeOfAPartnerHostIsForeign() =>
        Assert.Equal(
            RequestOrigin.Foreign,
            HotlinkPolicy.Classify(
                Request(referer: "https://notpartner.example.com/page", fetchSite: "cross-site"),
                Site, Partners));

    // --- Referer, used when Sec-Fetch-Site is absent ---

    [Fact]
    public void NoHeadersAtAllIsUnknown() =>
        Assert.Equal(RequestOrigin.Unknown, HotlinkPolicy.Classify(Request(), Site, None));

    [Fact]
    public void OurOwnRefererIsAllowed() =>
        Assert.Equal(
            RequestOrigin.Allowed,
            HotlinkPolicy.Classify(Request(referer: $"https://{SiteHost}/post"), Site, None));

    [Fact]
    public void TheHostTheRequestArrivedOnCountsAsOurOwn() =>
        Assert.Equal(
            RequestOrigin.Allowed,
            HotlinkPolicy.Classify(
                Request(referer: "https://localhost:8088/post", host: "localhost"),
                None, None));

    [Fact]
    public void ASubdomainOfOurOwnHostIsAllowed() =>
        Assert.Equal(
            RequestOrigin.Allowed,
            HotlinkPolicy.Classify(Request(referer: $"https://www.{SiteHost}/post"), Site, None));

    [Fact]
    public void AForeignRefererIsForeign() =>
        Assert.Equal(
            RequestOrigin.Foreign,
            HotlinkPolicy.Classify(Request(referer: "https://evil.example.com/leech"), Site, None));

    [Fact]
    public void AnUnparseableRefererIsForeignRatherThanMissing() =>
        Assert.Equal(
            RequestOrigin.Foreign,
            HotlinkPolicy.Classify(Request(referer: "not a url"), Site, None));

    [Fact]
    public void AnEmptyRefererIsTreatedAsAbsent() =>
        Assert.Equal(
            RequestOrigin.Unknown,
            HotlinkPolicy.Classify(Request(referer: string.Empty), Site, None));

    // --- Turning a verdict into a decision ---

    [Theory]
    [InlineData(HotlinkProtection.Off, RequestOrigin.Allowed, true)]
    [InlineData(HotlinkProtection.Off, RequestOrigin.Unknown, true)]
    [InlineData(HotlinkProtection.Off, RequestOrigin.Foreign, true)]
    [InlineData(HotlinkProtection.Lenient, RequestOrigin.Allowed, true)]
    [InlineData(HotlinkProtection.Lenient, RequestOrigin.Unknown, true)]
    [InlineData(HotlinkProtection.Lenient, RequestOrigin.Foreign, false)]
    [InlineData(HotlinkProtection.Strict, RequestOrigin.Allowed, true)]
    [InlineData(HotlinkProtection.Strict, RequestOrigin.Unknown, false)]
    [InlineData(HotlinkProtection.Strict, RequestOrigin.Foreign, false)]
    public void ProtectionDecidesWhichVerdictsPass(
        HotlinkProtection protection, RequestOrigin origin, bool allowed) =>
        Assert.Equal(allowed, HotlinkPolicy.IsAllowed(origin, protection));

    // --- Host list parsing ---

    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("https://example.com/some/path", "example.com")]
    [InlineData("EXAMPLE.com", "example.com")]
    [InlineData("example.com:8080", "example.com")]
    [InlineData("  example.com  ", "example.com")]
    public void ParseHostsNormalisesASingleEntry(string input, string expected) =>
        Assert.Equal([expected], HotlinkPolicy.ParseHosts(input));

    [Theory]
    [InlineData("a.com, b.com")]
    [InlineData("a.com;b.com")]
    [InlineData("a.com\nb.com")]
    [InlineData("a.com b.com")]
    [InlineData("a.com,,  b.com")]
    public void ParseHostsAcceptsEverySeparator(string input) =>
        Assert.Equal(["a.com", "b.com"], HotlinkPolicy.ParseHosts(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseHostsYieldsNothingForEmptyInput(string? input) =>
        Assert.Empty(HotlinkPolicy.ParseHosts(input));
}
