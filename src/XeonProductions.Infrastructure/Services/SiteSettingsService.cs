using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

public class SiteSettingsService(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache) : ISiteSettingsService
{
    private const string CacheKey = "site-settings";

    public async Task<SiteSettings> GetAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out SiteSettings? cached) && cached is not null)
            return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var rows = await db.SiteSettings.AsNoTracking()
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);

        var settings = new SiteSettings();
        foreach (var prop in typeof(SiteSettings).GetProperties())
        {
            if (!rows.TryGetValue(prop.Name, out var raw) || raw is null) continue;
            try
            {
                var target = Nullable.GetUnderlyingType(prop.PropertyType) ?? prop.PropertyType;
                object? value = target.IsEnum
                    ? Enum.Parse(target, raw)
                    : Convert.ChangeType(raw, target);
                prop.SetValue(settings, value);
            }
            catch
            {
                // A malformed row must never take the whole site down; keep the default.
            }
        }

        // Publishing dates and permalinks read this statically, so keep it in step.
        SiteTime.Configure(settings.SiteTimeZone);

        cache.Set(CacheKey, settings, TimeSpan.FromMinutes(10));
        return settings;
    }

    public async Task SaveAsync(SiteSettings settings, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.SiteSettings.ToDictionaryAsync(x => x.Key, ct);

        foreach (var prop in typeof(SiteSettings).GetProperties())
        {
            var value = prop.GetValue(settings)?.ToString();

            if (existing.TryGetValue(prop.Name, out var row))
            {
                if (row.Value == value) continue;
                row.Value = value;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                db.SiteSettings.Add(new SiteSetting { Key = prop.Name, Value = value });
            }
        }

        await db.SaveChangesAsync(ct);
        Invalidate();
    }

    public void Invalidate() => cache.Remove(CacheKey);
}
