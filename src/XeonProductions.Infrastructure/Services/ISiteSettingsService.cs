namespace XeonProductions.Infrastructure.Services;

public interface ISiteSettingsService
{
    Task<SiteSettings> GetAsync(CancellationToken ct = default);
    Task SaveAsync(SiteSettings settings, CancellationToken ct = default);
    void Invalidate();
}
