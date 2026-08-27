using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Data;

namespace XeonProductions.Infrastructure.Services;

public interface INavigationService
{
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(MenuLocation location, CancellationToken ct = default);
    Task<IReadOnlyList<Widget>> GetWidgetsAsync(WidgetArea area, CancellationToken ct = default);
    void Invalidate();
}

/// <summary>
/// Menus and widgets render on every request and change rarely, so both are cached until
/// the admin edits them.
/// </summary>
public class NavigationService(
    IDbContextFactory<AppDbContext> dbFactory,
    IMemoryCache cache) : INavigationService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlyList<MenuItem>> GetMenuAsync(MenuLocation location, CancellationToken ct = default)
    {
        var key = $"menu:{location}";
        if (cache.TryGetValue(key, out IReadOnlyList<MenuItem>? cached) && cached is not null)
            return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var items = await db.MenuItems.AsNoTracking()
            .Where(i => i.Menu != null && i.Menu.Location == location)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.Label)
            .ToListAsync(ct);

        // Rebuild the tree in memory; menus are far too small to justify a recursive query.
        var byId = items.ToDictionary(i => i.Id);
        var roots = new List<MenuItem>();

        foreach (var item in items)
        {
            item.Children = [];
        }

        foreach (var item in items)
        {
            if (item.ParentId is int pid && byId.TryGetValue(pid, out var parent))
                parent.Children.Add(item);
            else
                roots.Add(item);
        }

        cache.Set(key, (IReadOnlyList<MenuItem>)roots, Ttl);
        return roots;
    }

    public async Task<IReadOnlyList<Widget>> GetWidgetsAsync(WidgetArea area, CancellationToken ct = default)
    {
        var key = $"widgets:{area}";
        if (cache.TryGetValue(key, out IReadOnlyList<Widget>? cached) && cached is not null)
            return cached;

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var widgets = await db.Widgets.AsNoTracking()
            .Where(w => w.Area == area && w.IsActive)
            .OrderBy(w => w.SortOrder)
            .Include(w => w.Links.OrderBy(l => l.SortOrder))
            .ToListAsync(ct);

        cache.Set(key, (IReadOnlyList<Widget>)widgets, Ttl);
        return widgets;
    }

    public void Invalidate()
    {
        foreach (var location in Enum.GetValues<MenuLocation>())
            cache.Remove($"menu:{location}");

        foreach (var area in Enum.GetValues<WidgetArea>())
            cache.Remove($"widgets:{area}");
    }
}
