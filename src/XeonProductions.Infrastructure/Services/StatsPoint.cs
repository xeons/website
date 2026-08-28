namespace XeonProductions.Infrastructure.Services;

/// <summary>One day on the time series.</summary>
public record StatsPoint(DateOnly Day, int Views, int Visitors);
