using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Stores uploads on disk and records them in the database.
///
/// Image work is done with SkiaSharp. The original bytes are written through untouched
/// unless the image is actually too wide, which keeps animated GIFs, colour profiles and
/// transparency intact for the common case.
/// </summary>
public class MediaService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<MediaOptions> options,
    IMemoryCache cache,
    ILogger<MediaService> logger) : IMediaService
{
    private readonly MediaOptions _opts = options.Value;

    public async Task<MediaVariants?> ResolveByUrlAsync(string? url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var prefix = _opts.PublicBasePath.TrimEnd('/') + "/";
        if (!url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var relativePath = url[prefix.Length..];

        // The logo is resolved on every page render, so the answer is cached. It only
        // changes when the image itself is replaced.
        var key = $"media-variants:{relativePath}";

        if (cache.TryGetValue(key, out MediaVariants? cached)) return cached;

        MediaVariants? variants = null;

        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var item = await db.Media.AsNoTracking()
                .FirstOrDefaultAsync(m => m.RelativePath == relativePath, ct);

            if (item is not null)
            {
                variants = new MediaVariants(
                    PublicUrl(item.RelativePath),
                    item.Width,
                    item.Height,
                    item.ThumbnailPath is null ? null : PublicUrl(item.ThumbnailPath),
                    _opts.ThumbnailWidth);
            }
        }
        catch (Exception ex)
        {
            // Never let this stop a page rendering; the caller falls back to the plain URL.
            logger.LogWarning(ex, "Could not resolve media for {Url}.", url);
        }

        cache.Set(key, variants, TimeSpan.FromMinutes(10));
        return variants;
    }

    public async Task<MediaUploadResult> SaveAsync(
        Stream content, string fileName, string contentType,
        string? uploadedById = null, string? sourceUrl = null, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (!_opts.AllowedExtensions.Contains(ext))
        {
            return new MediaUploadResult(false, null, $"File type {ext} is not allowed.");
        }

        // Buffer first: the length is needed up front, and the bytes may be read twice.
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);

        if (buffer.Length == 0)
        {
            return new MediaUploadResult(false, null, "File is empty.");
        }

        if (buffer.Length > _opts.MaxFileSizeBytes)
        {
            return new MediaUploadResult(
                false, null, $"File exceeds {_opts.MaxFileSizeBytes / 1024 / 1024} MB.");
        }

        var bytes = buffer.ToArray();

        var now = DateTimeOffset.UtcNow;
        var folder = $"{now:yyyy}/{now:MM}";

        var safeName = SlugHelper.Slugify(Path.GetFileNameWithoutExtension(fileName), 100);
        if (string.IsNullOrEmpty(safeName)) safeName = "file";

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var relativePath = await NextFreePathAsync(db, folder, safeName, ext, ct);
        var absolutePath = Path.Combine(_opts.StorageRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        int? width = null, height = null;
        string? thumbnailPath = null;

        // SVG is an image that Skia will not decode, so it is stored verbatim.
        var isRaster = contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                       && ext is not (".svg" or ".ico");

        if (isRaster)
        {
            var processed = ProcessImage(bytes, ext, relativePath, absolutePath);

            width = processed.Width;
            height = processed.Height;
            thumbnailPath = processed.ThumbnailPath;

            if (!processed.Handled)
            {
                await File.WriteAllBytesAsync(absolutePath, bytes, ct);
            }
        }
        else
        {
            await File.WriteAllBytesAsync(absolutePath, bytes, ct);
        }

        var item = new MediaItem
        {
            FileName = fileName,
            RelativePath = relativePath.Replace('\\', '/'),
            ThumbnailPath = thumbnailPath,
            ContentType = contentType,
            SizeBytes = new FileInfo(absolutePath).Length,
            Width = width,
            Height = height,
            Title = Path.GetFileNameWithoutExtension(fileName),
            SourceUrl = sourceUrl,
            UploadedById = uploadedById,
            UploadedAt = now
        };

        db.Media.Add(item);
        await db.SaveChangesAsync(ct);

        return new MediaUploadResult(true, item, null);
    }

    private record ImageResult(bool Handled, int? Width, int? Height, string? ThumbnailPath);

    /// <summary>
    /// Reads the dimensions, downscales when the image is wider than the configured maximum,
    /// and writes a WebP thumbnail. Returns Handled = false when the caller should just write
    /// the original bytes, which is the usual path.
    /// </summary>
    private ImageResult ProcessImage(byte[] bytes, string ext, string relativePath, string absolutePath)
    {
        SKBitmap? decoded = null;

        try
        {
            decoded = SKBitmap.Decode(bytes);

            if (decoded is null)
            {
                // Not something Skia understands. Store it as uploaded rather than reject it.
                logger.LogDebug("Could not decode {Path} as an image; storing it unchanged.", relativePath);
                return new ImageResult(false, null, null, null);
            }

            var width = decoded.Width;
            var height = decoded.Height;

            var needsDownscale = width > _opts.MaxImageWidth;
            var encodeFormat = FormatFor(ext);

            // Re-encoding a GIF would flatten it to a single frame, so a large GIF is left
            // at its original size instead.
            if (needsDownscale && encodeFormat is null)
            {
                needsDownscale = false;
            }

            SKBitmap working = decoded;
            SKBitmap? resized = null;

            try
            {
                if (needsDownscale)
                {
                    var targetHeight = Math.Max(1, (int)Math.Round(height * (_opts.MaxImageWidth / (double)width)));
                    resized = Resize(decoded, _opts.MaxImageWidth, targetHeight);

                    if (resized is not null)
                    {
                        working = resized;
                        width = working.Width;
                        height = working.Height;

                        Write(working, encodeFormat!.Value, absolutePath, quality: 90);
                    }
                    else
                    {
                        needsDownscale = false;
                    }
                }

                var thumbnailPath = WriteThumbnail(working, relativePath);

                return new ImageResult(needsDownscale, width, height, thumbnailPath);
            }
            finally
            {
                resized?.Dispose();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Image processing failed for {Path}; storing the original.", relativePath);
            return new ImageResult(false, null, null, null);
        }
        finally
        {
            decoded?.Dispose();
        }
    }

    private string? WriteThumbnail(SKBitmap source, string relativePath)
    {
        if (source.Width <= _opts.ThumbnailWidth) return null;

        var targetHeight = Math.Max(1,
            (int)Math.Round(source.Height * (_opts.ThumbnailWidth / (double)source.Width)));

        using var thumb = Resize(source, _opts.ThumbnailWidth, targetHeight);
        if (thumb is null) return null;

        var dir = Path.GetDirectoryName(relativePath)!.Replace('\\', '/');
        var thumbRelative = $"{dir}/{Path.GetFileNameWithoutExtension(relativePath)}-thumb.webp";
        var thumbAbsolute = Path.Combine(_opts.StorageRoot, thumbRelative);

        Directory.CreateDirectory(Path.GetDirectoryName(thumbAbsolute)!);
        Write(thumb, SKEncodedImageFormat.Webp, thumbAbsolute, _opts.ThumbnailQuality);

        return thumbRelative;
    }

    private static SKBitmap? Resize(SKBitmap source, int width, int height)
    {
        var info = new SKImageInfo(width, height, source.ColorType, source.AlphaType);

        // Mitchell cubic resampling: noticeably better than bilinear on screenshots and text,
        // which is most of what this site publishes.
        return source.Resize(info, new SKSamplingOptions(SKCubicResampler.Mitchell));
    }

    private static void Write(SKBitmap bitmap, SKEncodedImageFormat format, string path, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        using var file = File.Create(path);

        data.SaveTo(file);
    }

    /// <summary>Null means Skia cannot write this format, so the original must be kept.</summary>
    private static SKEncodedImageFormat? FormatFor(string ext) => ext switch
    {
        ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
        ".png" => SKEncodedImageFormat.Png,
        ".webp" => SKEncodedImageFormat.Webp,
        _ => null
    };

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var item = await db.Media.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (item is null) return false;

        // Remove the row first: an orphaned file is harmless, an orphaned row is not.
        db.Media.Remove(item);
        await db.SaveChangesAsync(ct);

        foreach (var path in new[] { item.RelativePath, item.ThumbnailPath })
        {
            if (string.IsNullOrEmpty(path)) continue;

            try
            {
                var absolute = Path.Combine(_opts.StorageRoot, path);
                if (File.Exists(absolute)) File.Delete(absolute);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Deleted media row {Id} but could not remove {Path}.", id, path);
            }
        }

        return true;
    }

    public string PublicUrl(string? relativePath) =>
        string.IsNullOrEmpty(relativePath)
            ? string.Empty
            : $"{_opts.PublicBasePath.TrimEnd('/')}/{relativePath.TrimStart('/')}";

    public string ThumbnailUrl(MediaItem item) =>
        PublicUrl(item.ThumbnailPath ?? item.RelativePath);

    private async Task<string> NextFreePathAsync(
        AppDbContext db, string folder, string name, string ext, CancellationToken ct)
    {
        var candidate = $"{folder}/{name}{ext}";
        var attempt = 2;

        while (await db.Media.AnyAsync(m => m.RelativePath == candidate, ct)
               || File.Exists(Path.Combine(_opts.StorageRoot, candidate)))
        {
            candidate = $"{folder}/{name}-{attempt++}{ext}";
            if (attempt > 500) candidate = $"{folder}/{name}-{Guid.NewGuid():N}{ext}";
        }

        return candidate;
    }
}
