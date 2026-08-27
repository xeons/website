using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using XeonProductions.Web.Services;

namespace XeonProductions.Tests.Services;

public class DownloadLinkSignerTests
{
    private static DownloadLinkSigner NewSigner() =>
        new(new EphemeralDataProtectionProvider());

    private static HttpContext Context(string ip = "203.0.113.7", string agent = "Mozilla/5.0")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Headers.UserAgent = agent;
        return context;
    }

    [Fact]
    public void ATokenRoundTripsForTheClientItWasIssuedTo()
    {
        var signer = NewSigner();
        var key = signer.ClientKey(Context());

        var token = signer.Issue(42, key, TimeSpan.FromMinutes(30));

        Assert.Equal(42, signer.Validate(token, key));
    }

    [Fact]
    public void ATokenIsRefusedForADifferentClient()
    {
        var signer = NewSigner();

        var token = signer.Issue(42, signer.ClientKey(Context(ip: "203.0.113.7")), TimeSpan.FromMinutes(30));

        Assert.Null(signer.Validate(token, signer.ClientKey(Context(ip: "198.51.100.9"))));
    }

    [Fact]
    public void ATokenIsRefusedForADifferentUserAgent()
    {
        var signer = NewSigner();

        var token = signer.Issue(42, signer.ClientKey(Context(agent: "Mozilla/5.0")), TimeSpan.FromMinutes(30));

        Assert.Null(signer.Validate(token, signer.ClientKey(Context(agent: "curl/8.0"))));
    }

    [Fact]
    public void AnExpiredTokenIsRefused()
    {
        var signer = NewSigner();
        var key = signer.ClientKey(Context());

        // A negative lifetime places the expiry in the past.
        var token = signer.Issue(42, key, TimeSpan.FromSeconds(-1));

        Assert.Null(signer.Validate(token, key));
    }

    [Fact]
    public void ATokenFromADifferentKeyRingIsRefused()
    {
        var key = NewSigner().ClientKey(Context());
        var token = NewSigner().Issue(42, key, TimeSpan.FromMinutes(30));

        Assert.Null(NewSigner().Validate(token, key));
    }

    /// <summary>
    /// The token comes straight from the URL, so malformed input must be a refusal rather
    /// than an exception.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-token")]
    [InlineData("!!!!not base64!!!!")]
    [InlineData("CfDJ8AAAAAAAAAAAAAAAAAAAAAA")]
    public void MalformedTokensAreRefusedWithoutThrowing(string token) =>
        Assert.Null(NewSigner().Validate(token, "any-client-key"));

    [Fact]
    public void TheClientKeyIsStableForTheSameClient()
    {
        var signer = NewSigner();

        Assert.Equal(signer.ClientKey(Context()), signer.ClientKey(Context()));
    }

    [Fact]
    public void TheClientKeyRevealsNeitherTheAddressNorTheAgent()
    {
        var key = NewSigner().ClientKey(Context(ip: "203.0.113.7", agent: "Mozilla/5.0"));

        Assert.DoesNotContain("203.0.113.7", key);
        Assert.DoesNotContain("Mozilla", key);
    }

    // --- Rate limit keys ---

    [Fact]
    public void AnIPv4AddressIsCountedOnItsOwn() =>
        Assert.Equal("203.0.113.7", NewSigner().RateLimitKey(Context(ip: "203.0.113.7")));

    [Fact]
    public void AnIPv4MappedAddressIsCountedAsIPv4() =>
        Assert.Equal("203.0.113.7", NewSigner().RateLimitKey(Context(ip: "::ffff:203.0.113.7")));

    /// <summary>
    /// A client holding a whole prefix must not be able to walk through a per-address limit
    /// by using a fresh address each time.
    /// </summary>
    [Fact]
    public void IPv6AddressesInOnePrefixShareARateLimitKey()
    {
        var signer = NewSigner();

        var first = signer.RateLimitKey(Context(ip: "2001:db8:abcd:1234::1"));
        var second = signer.RateLimitKey(Context(ip: "2001:db8:abcd:1234:ffff:ffff:ffff:ffff"));

        Assert.Equal(first, second);
        Assert.EndsWith("::/64", first);
    }

    [Fact]
    public void SeparateIPv6PrefixesGetSeparateKeys()
    {
        var signer = NewSigner();

        Assert.NotEqual(
            signer.RateLimitKey(Context(ip: "2001:db8:abcd:1234::1")),
            signer.RateLimitKey(Context(ip: "2001:db8:abcd:9999::1")));
    }

    [Fact]
    public void AMissingAddressStillYieldsAKey()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = null;

        Assert.Equal("unknown", NewSigner().RateLimitKey(context));
    }
}
