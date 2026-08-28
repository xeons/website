namespace XeonProductions.Infrastructure.Services;

/// <summary>Headline figures for a date range.</summary>
/// <param name="Views">Page views.</param>
/// <param name="Visitors">Distinct visitor hashes. Counted per day, so a visitor returning
/// on a later day counts again.</param>
/// <param name="Sessions">Distinct visits.</param>
/// <param name="BouncedSessions">Sessions with exactly one page view.</param>
/// <param name="AverageSeconds">Mean dwell time across views the beacon reported.</param>
/// <param name="MeasuredViews">Views the beacon reported, which the average is drawn from.</param>
public record StatsSummary(
    int Views,
    int Visitors,
    int Sessions,
    int BouncedSessions,
    double AverageSeconds,
    int MeasuredViews)
{
    public static readonly StatsSummary Empty = new(0, 0, 0, 0, 0, 0);

    public double BounceRate => Sessions == 0 ? 0 : BouncedSessions / (double)Sessions;

    public double ViewsPerSession => Sessions == 0 ? 0 : Views / (double)Sessions;
}
