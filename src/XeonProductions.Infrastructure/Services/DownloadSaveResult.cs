using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Services;

/// <summary>Outcome of storing an upload. <paramref name="Error"/> is set only on failure.</summary>
public record DownloadSaveResult(bool Success, Download? Item, string? Error);
