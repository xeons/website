using System.Globalization;
using System.Xml.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace XeonProductions.Infrastructure.Services;

public record FeedItem(string Title, string Link, DateTimeOffset? Published, string? Summary);

public interface IFeedReader
{
    /// <summary>
    /// Fetches and parses an external RSS or Atom feed. Returns an empty list on any failure:
    /// a widget must never be able to take the page down.
    /// </summary>
    Task<IReadOnlyList<FeedItem>> ReadAsync(string? url, int maxItems, CancellationToken ct = default);
}

public class FeedReader(
    IHttpClientFactory httpClientFactory,
    IMemoryCache cache,
    IHtmlService html,
    ILogger<FeedReader> logger) : IFeedReader
{
    /// <summary>External feeds are polled at most this often, however many visitors arrive.</summary>
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(20);

    /// <summary>Kept short: a slow feed must not hold up rendering the sidebar.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    public async Task<IReadOnlyList<FeedItem>> ReadAsync(
        string? url, int maxItems, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return [];

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            logger.LogWarning("Feed widget has an invalid URL: {Url}", url);
            return [];
        }

        var key = $"feed:{uri}:{maxItems}";
        if (cache.TryGetValue(key, out IReadOnlyList<FeedItem>? cached) && cached is not null)
        {
            return cached;
        }

        var items = await FetchAsync(uri, maxItems, ct);

        // Cache failures too, briefly, so an unreachable host is not retried on every request.
        cache.Set(key, items, items.Count > 0 ? CacheFor : TimeSpan.FromMinutes(5));
        return items;
    }

    private async Task<IReadOnlyList<FeedItem>> FetchAsync(Uri uri, int maxItems, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);

            var client = httpClientFactory.CreateClient("feeds");

            using var response = await client.GetAsync(uri, timeout.Token);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var document = await XDocument.LoadAsync(stream, LoadOptions.None, timeout.Token);

            return Parse(document, maxItems);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read the feed at {Url}.", uri);
            return [];
        }
    }

    private IReadOnlyList<FeedItem> Parse(XDocument document, int maxItems)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";

        // RSS 2.0 uses channel/item; Atom uses feed/entry. GitHub serves Atom.
        var entries = document.Root?.Element("channel")?.Elements("item")
                      ?? document.Root?.Elements(atom + "entry")
                      ?? [];

        var items = new List<FeedItem>();

        foreach (var entry in entries)
        {
            var isAtom = entry.Name.Namespace == atom;

            var title = Collapse(isAtom
                ? entry.Element(atom + "title")?.Value
                : entry.Element("title")?.Value);

            if (string.IsNullOrWhiteSpace(title)) continue;

            var link = isAtom
                // Atom puts the URL in an attribute, and may list several relations.
                ? (entry.Elements(atom + "link")
                        .FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")
                        ?.Attribute("href")?.Value)
                : entry.Element("link")?.Value;

            if (string.IsNullOrWhiteSpace(link)) continue;

            var rawDate = isAtom
                ? entry.Element(atom + "published")?.Value ?? entry.Element(atom + "updated")?.Value
                : entry.Element("pubDate")?.Value;

            var published = ParseDate(rawDate);

            var rawSummary = isAtom
                ? entry.Element(atom + "summary")?.Value ?? entry.Element(atom + "content")?.Value
                : entry.Element("description")?.Value;

            // Feed content is third-party HTML and is only ever shown as plain text.
            var summary = Collapse(html.ToPlainText(rawSummary));
            if (summary?.Length > 160) summary = summary[..160].TrimEnd() + "...";

            items.Add(new FeedItem(title!, link!, published, summary));

            if (items.Count >= maxItems) break;
        }

        return items;
    }

    /// <summary>
    /// Feeds are inconsistent about dates. Atom is supposed to be ISO 8601 and RSS uses
    /// RFC 822, but GitHub writes "2026-08-24 00:38:18 UTC", which is neither, so a plain
    /// TryParse silently drops it and the item renders with no date at all.
    /// </summary>
    private static DateTimeOffset? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var value = raw.Trim();

        const DateTimeStyles styles = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, styles, out var parsed))
        {
            return parsed;
        }

        // Strip a trailing zone name, which no standard parser accepts.
        var withoutZone = value;
        foreach (var suffix in new[] { " UTC", " GMT", " Z" })
        {
            if (withoutZone.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                withoutZone = withoutZone[..^suffix.Length].Trim();
                break;
            }
        }

        string[] formats =
        [
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd"
        ];

        if (DateTime.TryParseExact(withoutZone, formats, CultureInfo.InvariantCulture, styles, out var exact))
        {
            return new DateTimeOffset(exact, TimeSpan.Zero);
        }

        return null;
    }

    private static string? Collapse(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
