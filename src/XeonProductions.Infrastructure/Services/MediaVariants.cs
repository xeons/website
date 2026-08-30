namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// The sizes available for one stored image, resolved from a public URL. Used where an
/// image is referenced by URL rather than by id, such as the site logo.
/// </summary>
public record MediaVariants(
    string Url,
    int? Width,
    int? Height,
    string? ThumbnailUrl,
    int ThumbnailWidth,
    IReadOnlyList<MediaVariant> Webp);
