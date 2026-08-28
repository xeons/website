using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class StatsSummaryTests
{
    [Fact]
    public void BounceRateIsTheShareOfSingleViewVisits()
    {
        var summary = new StatsSummary(
            Views: 140, Visitors: 80, Sessions: 100,
            BouncedSessions: 58, AverageSeconds: 90, MeasuredViews: 120);

        Assert.Equal(0.58, summary.BounceRate, 3);
        Assert.Equal(1.4, summary.ViewsPerSession, 3);
    }

    /// <summary>An empty range must report zero rather than divide by it.</summary>
    [Fact]
    public void AnEmptyRangeDividesByNothing()
    {
        Assert.Equal(0, StatsSummary.Empty.BounceRate);
        Assert.Equal(0, StatsSummary.Empty.ViewsPerSession);
    }

    [Fact]
    public void EverySessionBouncingIsAFullRate()
    {
        var summary = new StatsSummary(10, 10, 10, 10, 0, 0);

        Assert.Equal(1.0, summary.BounceRate);
    }

    /// <summary>
    /// The average is drawn only from views the beacon reported, so it is recorded separately
    /// from the view count. A summary where nothing was measured still reports its views.
    /// </summary>
    [Fact]
    public void ViewsAreCountedEvenWhenNoDwellTimeWasReported()
    {
        var summary = new StatsSummary(
            Views: 500, Visitors: 300, Sessions: 350,
            BouncedSessions: 100, AverageSeconds: 0, MeasuredViews: 0);

        Assert.Equal(500, summary.Views);
        Assert.Equal(0, summary.MeasuredViews);
        Assert.Equal(0, summary.AverageSeconds);
    }
}
