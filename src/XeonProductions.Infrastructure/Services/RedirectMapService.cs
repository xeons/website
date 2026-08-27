using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

public record RedirectRule(int Id, string ToUrl, int StatusCode);

public interface IRedirectMap
{
    Task<RedirectRule?> FindAsync(string path, CancellationToken ct = default);
    void Invalidate();
}

/// <summary>
/// The redirect table is consulted on every request, so it is held in memory as a dictionary
/// and only re-read when the admin changes it. A site has tens of rules, not thousands.
/// </summary>
public class RedirectMap(IServiceProvider services, IMemoryCache cache) : IRedirectMap
{
    private const string CacheKey = "redirect-map";

    public async Task<RedirectRule?> FindAsync(string path, CancellationToken ct = default)
    {
        var map = await GetMapAsync(ct);
        return map.GetValueOrDefault(path);
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<IReadOnlyDictionary<string, RedirectRule>> GetMapAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyDictionary<string, RedirectRule>? cached)
            && cached is not null)
        {
            return cached;
        }

        // Resolved from the root provider: this runs before routing, from middleware that
        // must not depend on a particular request scope living long enough.
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var map = await db.Redirects.AsNoTracking()
            .Where(r => r.IsActive)
            .ToDictionaryAsync(
                r => r.FromPath,
                r => new RedirectRule(r.Id, r.ToUrl, r.StatusCode),
                StringComparer.OrdinalIgnoreCase,
                ct);

        cache.Set(CacheKey, (IReadOnlyDictionary<string, RedirectRule>)map, TimeSpan.FromMinutes(30));
        return map;
    }
}
