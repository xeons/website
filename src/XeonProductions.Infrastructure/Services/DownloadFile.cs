namespace XeonProductions.Infrastructure.Services;

/// <summary>A stored file on disk and the metadata the transfer response needs.</summary>
public record DownloadFile(string AbsolutePath, long Length, DateTimeOffset LastModified, string EntityTag);
