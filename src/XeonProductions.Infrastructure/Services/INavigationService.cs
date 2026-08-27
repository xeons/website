using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;

namespace XeonProductions.Infrastructure.Services;

public interface INavigationService
{
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(MenuLocation location, CancellationToken ct = default);
    Task<IReadOnlyList<Widget>> GetWidgetsAsync(WidgetArea area, CancellationToken ct = default);
    void Invalidate();
}
