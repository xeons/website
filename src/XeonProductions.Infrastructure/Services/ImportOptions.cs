namespace XeonProductions.Infrastructure.Services;

public record ImportOptions
{
    /// <summary>Base URL of the WordPress site, without a trailing slash.</summary>
    public string SourceUrl { get; init; } = "https://xeonproductions.com";

    /// <summary>Download attachments into the local media store.</summary>
    public bool ImportMedia { get; init; } = true;

    /// <summary>Report what would happen without writing anything.</summary>
    public bool DryRun { get; init; }

    /// <summary>Re-import entries that already exist, overwriting their content.</summary>
    public bool Overwrite { get; init; }

    /// <summary>Author to attribute imported content to. Falls back to the first account.</summary>
    public string? AuthorId { get; init; }
}
