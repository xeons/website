using XeonProductions.Domain.Entities;

namespace XeonProductions.Infrastructure.Services;

public record MediaUploadResult(bool Success, MediaItem? Item, string? Error);
