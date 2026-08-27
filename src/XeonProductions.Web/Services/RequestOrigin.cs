namespace XeonProductions.Web.Services;

/// <summary>Where a request appears to have come from, on the headers available.</summary>
public enum RequestOrigin
{
    /// <summary>Followed from this site, or from a host permitted to link here.</summary>
    Allowed,

    /// <summary>Followed from somewhere else.</summary>
    Foreign,

    /// <summary>No usable evidence: typed in, bookmarked, or the headers were suppressed.</summary>
    Unknown
}
