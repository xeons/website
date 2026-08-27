using XeonProductions.Web.Services;

namespace XeonProductions.Tests.Services;

public class DownloadTrafficGuardTests
{
    private const string Client = "203.0.113.7";
    private const string Other = "198.51.100.9";

    [Fact]
    public void BothLimitsDisabledAllowsEverythingAndTakesNoSlot()
    {
        var guard = new DownloadTrafficGuard();

        for (var i = 0; i < 50; i++)
        {
            var decision = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 0);

            Assert.True(decision.Allowed);
            Assert.Null(decision.Slot);
        }
    }

    [Fact]
    public void ASecondSimultaneousTransferIsRefused()
    {
        var guard = new DownloadTrafficGuard();

        var first = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 1);
        var second = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 1);

        Assert.True(first.Allowed);
        Assert.False(second.Allowed);
        Assert.Equal(TrafficVerdict.TooManyConcurrent, second.Verdict);
        Assert.True(second.RetryAfterSeconds > 0);
    }

    [Fact]
    public void DisposingASlotReleasesTheConcurrencyAllowance()
    {
        var guard = new DownloadTrafficGuard();

        var first = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 1);
        Assert.False(guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 1).Allowed);

        first.Slot!.Dispose();

        Assert.True(guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 1).Allowed);
    }

    /// <summary>
    /// The response stream can be disposed more than once on an error path, which must not
    /// hand the client extra allowance.
    /// </summary>
    [Fact]
    public void DisposingASlotTwiceReleasesOnlyOneAllowance()
    {
        var guard = new DownloadTrafficGuard();

        var first = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 2);
        var second = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 2);
        Assert.True(second.Allowed);

        first.Slot!.Dispose();
        first.Slot!.Dispose();

        Assert.True(guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 2).Allowed);
        Assert.False(guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 2).Allowed);

        second.Slot!.Dispose();
    }

    [Fact]
    public void TransfersBeyondTheHourlyLimitAreRefused()
    {
        var guard = new DownloadTrafficGuard();

        Assert.True(guard.TryStart(Client, maxPerHour: 2, maxConcurrent: 0).Allowed);
        Assert.True(guard.TryStart(Client, maxPerHour: 2, maxConcurrent: 0).Allowed);

        var third = guard.TryStart(Client, maxPerHour: 2, maxConcurrent: 0);

        Assert.False(third.Allowed);
        Assert.Equal(TrafficVerdict.TooManyRequests, third.Verdict);
        Assert.InRange(third.RetryAfterSeconds, 1, 3600);
    }

    /// <summary>
    /// Releasing a slot returns concurrency allowance but must not refund a start, or the
    /// hourly limit would never be reached.
    /// </summary>
    [Fact]
    public void ReleasingASlotDoesNotRefundAnHourlyStart()
    {
        var guard = new DownloadTrafficGuard();

        guard.TryStart(Client, maxPerHour: 2, maxConcurrent: 5).Slot?.Dispose();
        guard.TryStart(Client, maxPerHour: 2, maxConcurrent: 5).Slot?.Dispose();

        Assert.False(guard.TryStart(Client, maxPerHour: 2, maxConcurrent: 5).Allowed);
    }

    [Fact]
    public void OneClientCannotSpendAnothersAllowance()
    {
        var guard = new DownloadTrafficGuard();

        Assert.True(guard.TryStart(Client, maxPerHour: 1, maxConcurrent: 1).Allowed);
        Assert.False(guard.TryStart(Client, maxPerHour: 1, maxConcurrent: 1).Allowed);

        Assert.True(guard.TryStart(Other, maxPerHour: 1, maxConcurrent: 1).Allowed);
    }

    [Fact]
    public void ConcurrentCallersNeverExceedTheConcurrencyLimit()
    {
        var guard = new DownloadTrafficGuard();
        var granted = 0;

        Parallel.For(0, 200, _ =>
        {
            var decision = guard.TryStart(Client, maxPerHour: 0, maxConcurrent: 5);
            if (decision.Allowed) Interlocked.Increment(ref granted);
        });

        Assert.Equal(5, granted);
    }
}
