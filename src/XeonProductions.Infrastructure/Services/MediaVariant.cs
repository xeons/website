namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// One WebP copy of a stored image, at a width the browser can choose against.
/// </summary>
/// <param name="Url">Public URL of the copy.</param>
/// <param name="Width">Its width in pixels, which is what a srcset descriptor states.</param>
public record MediaVariant(string Url, int Width);
