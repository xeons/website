namespace XeonProductions.Infrastructure.Services;

/// <summary>One row of a ranked table: a page, referrer, country, browser and so on.</summary>
/// <param name="Key">The grouping value. Null means it was not recorded.</param>
/// <param name="Count">Views, or sessions where the table counts visits.</param>
/// <param name="AverageSeconds">Mean dwell time for the group, zero when nothing was measured.</param>
public record StatsBreakdown(string? Key, int Count, double AverageSeconds);
