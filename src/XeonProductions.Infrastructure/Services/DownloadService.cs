using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Stores download files on disk and records them in the database. Access rules live in the
/// web layer; this type deals only with files and rows.
/// </summary>
public class DownloadService(
    IDbContextFactory<AppDbContext> dbFactory,
    IOptions<DownloadOptions> options,
    ILogger<DownloadService> logger) : IDownloadService
{
    private readonly DownloadOptions _opts = options.Value;

    public string PublicUrl(Download item) => PublicUrl(item.Slug);

    public string PublicUrl(string slug) => $"{_opts.PublicBasePath.TrimEnd('/')}/{slug}";

    public async Task<DownloadSaveResult> CreateAsync(
        Stream content, string fileName, string contentType,
        string? title = null, string? uploadedById = null, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var display = string.IsNullOrWhiteSpace(title)
            ? Path.GetFileNameWithoutExtension(fileName)
            : title.Trim();

        if (string.IsNullOrWhiteSpace(display)) display = "Download";

        var slug = await SlugHelper.MakeUniqueAsync(
            SlugHelper.Slugify(display, 180),
            async candidate => await db.Downloads.AnyAsync(d => d.Slug == candidate, ct));

        var stored = await WriteAsync(content, fileName, slug, ct);
        if (stored.Error is not null) return new DownloadSaveResult(false, null, stored.Error);

        var item = new Download
        {
            Title = display,
            Slug = slug,
            FileName = SafeFileName(fileName),
            RelativePath = stored.RelativePath!,
            ContentType = string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType,
            SizeBytes = stored.Length,
            Sha256 = stored.Sha256,
            UploadedById = uploadedById,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Downloads.Add(item);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Stored download {Slug} ({Bytes} bytes) at {Path}.", slug, stored.Length, stored.RelativePath);

        return new DownloadSaveResult(true, item, null);
    }

    public async Task<DownloadSaveResult> ReplaceFileAsync(
        int id, Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var item = await db.Downloads.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (item is null) return new DownloadSaveResult(false, null, "That download no longer exists.");

        var previous = item.RelativePath;

        var stored = await WriteAsync(content, fileName, item.Slug, ct);
        if (stored.Error is not null) return new DownloadSaveResult(false, null, stored.Error);

        item.FileName = SafeFileName(fileName);
        item.RelativePath = stored.RelativePath!;
        item.ContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType;
        item.SizeBytes = stored.Length;
        item.Sha256 = stored.Sha256;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        // Only once the row points at the new file.
        TryDelete(previous);

        return new DownloadSaveResult(true, item, null);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var item = await db.Downloads.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (item is null) return false;

        db.Downloads.Remove(item);
        await db.SaveChangesAsync(ct);

        TryDelete(item.RelativePath);
        return true;
    }

    public Task<DownloadFile?> OpenAsync(Download item, CancellationToken ct = default)
    {
        if (!item.HasFile) return Task.FromResult<DownloadFile?>(null);

        var absolute = AbsolutePath(item.RelativePath);
        var info = new FileInfo(absolute);

        if (!info.Exists)
        {
            logger.LogError(
                "Download {Slug} points at {Path}, which is not on disk.", item.Slug, item.RelativePath);
            return Task.FromResult<DownloadFile?>(null);
        }

        var modified = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var tag = $"\"{item.Id:x}-{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"";

        return Task.FromResult<DownloadFile?>(new DownloadFile(absolute, info.Length, modified, tag));
    }

    public async Task CountHitAsync(int id, bool blocked, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            // A single UPDATE, so concurrent transfers do not lose a count.
            if (blocked)
            {
                await db.Downloads
                    .Where(d => d.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.BlockedCount, d => d.BlockedCount + 1), ct);
            }
            else
            {
                var now = DateTimeOffset.UtcNow;

                await db.Downloads
                    .Where(d => d.Id == id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(d => d.DownloadCount, d => d.DownloadCount + 1)
                        .SetProperty(d => d.LastDownloadedAt, now), ct);
            }
        }
        catch (Exception ex)
        {
            // Counting must never stop a file being served.
            logger.LogWarning(ex, "Could not record a hit against download {Id}.", id);
        }
    }

    /// <summary>
    /// Cleans a name that will be echoed back in a Content-Disposition header: removes any
    /// directory part and any control character.
    /// </summary>
    public static string SafeFileName(string? fileName)
    {
        var name = Path.GetFileName(fileName?.Replace('\\', '/') ?? string.Empty);

        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();

        return string.IsNullOrEmpty(name) ? "download" : name;
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:0.#} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:0.##} GB"
    };

    private string AbsolutePath(string relativePath) =>
        Path.Combine(_opts.StorageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private record StoredFile(string? RelativePath, long Length, string? Sha256, string? Error);

    /// <summary>
    /// Streams the upload to disk in chunks, hashing as it goes, and stops once the
    /// configured ceiling is passed. The declared Content-Length is not consulted; the limit
    /// applies to the bytes that arrive.
    /// </summary>
    private async Task<StoredFile> WriteAsync(
        Stream content, string fileName, string slug, CancellationToken ct)
    {
        var ext = Path.GetExtension(fileName);
        if (ext.Length > 20) ext = string.Empty;

        ext = new string(ext.Where(c => char.IsAsciiLetterOrDigit(c) || c == '.').ToArray())
            .ToLowerInvariant();

        var now = DateTimeOffset.UtcNow;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var relativePath = $"{now:yyyy}/{now:MM}/{slug}-{token}{ext}";

        var absolute = AbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        var buffer = new byte[_opts.BufferSize];
        long total = 0;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using (var file = new FileStream(
                absolute, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                _opts.BufferSize, useAsync: true))
            {
                int read;
                while ((read = await content.ReadAsync(buffer, ct)) > 0)
                {
                    total += read;

                    if (total > _opts.MaxFileSizeBytes)
                    {
                        throw new InvalidDataException(
                            $"File exceeds the {FormatSize(_opts.MaxFileSizeBytes)} limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }

            if (total == 0)
            {
                TryDelete(relativePath);
                return new StoredFile(null, 0, null, "The file was empty.");
            }

            var digest = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            return new StoredFile(relativePath, total, digest, null);
        }
        catch (InvalidDataException ex)
        {
            TryDelete(relativePath);
            return new StoredFile(null, 0, null, ex.Message);
        }
        catch (Exception ex)
        {
            // A cancelled or broken upload leaves a partial file behind.
            TryDelete(relativePath);
            logger.LogWarning(ex, "Upload of {Name} failed after {Bytes} bytes.", fileName, total);

            return new StoredFile(null, 0, null,
                ex is OperationCanceledException
                    ? "The upload was cancelled."
                    : "The upload could not be written to disk.");
        }
    }

    private void TryDelete(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return;

        try
        {
            var absolute = AbsolutePath(relativePath);
            if (File.Exists(absolute)) File.Delete(absolute);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not remove the download file {Path}.", relativePath);
        }
    }
}
