namespace XeonProductions.Domain.Entities;

/// <summary>
/// Key/value store behind the site settings service. One row per setting so the admin UI can
/// edit anything without a schema migration.
/// </summary>
public class SiteSetting
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
