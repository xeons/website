namespace XeonProductions.Infrastructure.Services;

public interface IFeedReader
{
    /// <summary>
    /// Fetches and parses an external RSS or Atom feed. Returns an empty list on any failure:
    /// a widget must never be able to take the page down.
    /// </summary>
    Task<IReadOnlyList<FeedItem>> ReadAsync(string? url, int maxItems, CancellationToken ct = default);
}
