namespace XeonProductions.Web.Services;

public enum TrafficVerdict
{
    Allowed,

    /// <summary>Too many transfers started from this address within the window.</summary>
    TooManyRequests,

    /// <summary>Too many transfers from this address are already running.</summary>
    TooManyConcurrent
}
