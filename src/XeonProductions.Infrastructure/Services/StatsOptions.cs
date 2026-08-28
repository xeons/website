namespace XeonProductions.Infrastructure.Services;

public class StatsOptions
{
    /// <summary>Turns capture off entirely. The admin screen still reads what was recorded.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Path to a MaxMind GeoLite2 country database. Blank leaves country unavailable; the
    /// file is not shipped with the application.
    /// </summary>
    public string? GeoDatabasePath { get; set; }

    /// <summary>Views older than this are pruned nightly. Zero keeps them forever.</summary>
    public int RetentionDays { get; set; } = 400;

    /// <summary>A gap longer than this starts a new session for the same visitor.</summary>
    public int SessionWindowMinutes { get; set; } = 30;

    /// <summary>Longest dwell time a beacon may report, so a tab left open does not skew it.</summary>
    public int MaxDurationSeconds { get; set; } = 1800;

    /// <summary>How often queued views are written, in milliseconds.</summary>
    public int FlushIntervalMs { get; set; } = 2000;

    /// <summary>Views held in memory before writes block. Overflow is dropped, not queued.</summary>
    public int QueueCapacity { get; set; } = 10000;

    /// <summary>Path prefixes that are never recorded.</summary>
    public string[] IgnoredPathPrefixes { get; set; } =
    [
        "/admin", "/media", "/download", "/health", "/api"
    ];

    /// <summary>Skip views from signed-in accounts, so your own visits are not counted.</summary>
    public bool IgnoreAuthenticated { get; set; } = true;
}
