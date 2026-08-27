namespace XeonProductions.Domain.Enums;

/// <summary>How strictly a download checks where the request came from.</summary>
public enum HotlinkProtection
{
    /// <summary>No referrer check. A signed transfer link is still issued.</summary>
    Off = 0,

    /// <summary>Refuses a request naming another site, allows one naming nothing.</summary>
    Lenient = 1,

    /// <summary>Requires a referrer from an allowed host.</summary>
    Strict = 2
}
