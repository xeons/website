namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Resolves the WordPress link forms that survive an export.
/// </summary>
public record LinkTargets
{
    /// <summary>Origins that belong to the old site, matched case-insensitively.</summary>
    public required IReadOnlySet<string> SourceHosts { get; init; }

    /// <summary>WordPress page id to the path it now lives at.</summary>
    public required IReadOnlyDictionary<int, string> PagePaths { get; init; }

    /// <summary>WordPress post id to its permalink.</summary>
    public required IReadOnlyDictionary<int, string> PostPermalinks { get; init; }

    /// <summary>Original attachment URL to the local media URL, for anything downloaded.</summary>
    public required IReadOnlyDictionary<string, string> MediaUrls { get; init; }
}
