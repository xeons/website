using Microsoft.Extensions.Caching.Memory;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class VisitorHasherTests
{
    /// <summary>
    /// Session bucketing reads no state, so the context factory is never touched. The visitor
    /// hash does read it, and is covered where a database is available.
    /// </summary>
    private static VisitorHasher Hasher() =>
        new(null!, new MemoryCache(new MemoryCacheOptions()));

    private static readonly DateTimeOffset Noon =
        new(2026, 8, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(30);

    [Fact]
    public void TheSameVisitorInTheSameWindowSharesASession()
    {
        var hasher = Hasher();

        Assert.Equal(
            hasher.Session("visitor-a", Noon, Window),
            hasher.Session("visitor-a", Noon.AddMinutes(5), Window));
    }

    [Fact]
    public void TwoVisitorsNeverShareASession()
    {
        var hasher = Hasher();

        Assert.NotEqual(
            hasher.Session("visitor-a", Noon, Window),
            hasher.Session("visitor-b", Noon, Window));
    }

    [Fact]
    public void AReturnAfterTheWindowStartsANewSession()
    {
        var hasher = Hasher();

        Assert.NotEqual(
            hasher.Session("visitor-a", Noon, Window),
            hasher.Session("visitor-a", Noon.AddHours(3), Window));
    }

    [Fact]
    public void ASessionIdRevealsNothingAboutTheVisitor()
    {
        var session = Hasher().Session("visitor-a", Noon, Window);

        Assert.DoesNotContain("visitor-a", session);
        Assert.Matches("^[0-9a-f]+$", session);
    }

    /// <summary>The column is 32 characters, so the value must fit whatever is fed in.</summary>
    [Theory]
    [InlineData("a")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    public void ASessionIdFitsItsColumn(string visitor)
    {
        var session = Hasher().Session(visitor, Noon, Window);

        Assert.True(session.Length <= 32);
        Assert.NotEmpty(session);
    }

    [Fact]
    public void AZeroWindowDoesNotDivideByZero()
    {
        var session = Hasher().Session("visitor-a", Noon, TimeSpan.Zero);

        Assert.NotEmpty(session);
    }
}
