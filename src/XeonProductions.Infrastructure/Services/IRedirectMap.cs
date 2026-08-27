namespace XeonProductions.Infrastructure.Services;

public interface IRedirectMap
{
    Task<RedirectRule?> FindAsync(string path, CancellationToken ct = default);
    void Invalidate();
}
