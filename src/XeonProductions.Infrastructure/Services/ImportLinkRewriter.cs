using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Points imported content at this site instead of the old one.
///
/// Four things need fixing. Links and images are written as absolute URLs to the WordPress
/// host, which would keep sending visitors back to the old site. Some links use the
/// ?page_id=N form, which means nothing here. Attachment URLs point at /wp-content, a path
/// this application does not serve. And images carry a srcset listing the resized copies
/// WordPress generated, none of which exist here.
/// </summary>
public partial class ImportLinkRewriter(ILogger<ImportLinkRewriter> logger) : IImportLinkRewriter
{
    private readonly HtmlParser _parser = new();

    public string Rewrite(string? html, LinkTargets targets)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var document = _parser.ParseDocument($"<body>{html}</body>");
        var media = new MediaIndex(targets.MediaUrls);
        var changed = false;

        foreach (var element in document.QuerySelectorAll("a[href], img[src], source[src], img[srcset], source[srcset]"))
        {
            changed |= RewriteAttribute(element, "href", targets, media);
            changed |= RewriteAttribute(element, "src", targets, media);

            // WordPress generated the srcset from resized copies that were never imported,
            // so every candidate in it is a dead URL here. The one image we do have is
            // already in src; dropping the set is better than pointing at files that
            // do not exist.
            if (element.HasAttribute("srcset") && element.GetAttribute("src") is { } src
                && src.StartsWith('/'))
            {
                element.RemoveAttribute("srcset");
                element.RemoveAttribute("sizes");
                changed = true;
            }
        }

        return changed ? document.Body?.InnerHtml ?? html : html;
    }

    private bool RewriteAttribute(IElement element, string attribute, LinkTargets targets, MediaIndex media)
    {
        var value = element.GetAttribute(attribute);
        if (string.IsNullOrWhiteSpace(value)) return false;

        var rewritten = Resolve(value, targets, media);
        if (rewritten is null || rewritten == value) return false;

        element.SetAttribute(attribute, rewritten);

        // A link that now points at this site should not open in a new tab or carry
        // rel attributes meant for outbound links.
        if (attribute == "href" && rewritten.StartsWith('/'))
        {
            element.RemoveAttribute("target");

            var rel = element.GetAttribute("rel");
            if (rel is not null && rel.Contains("noopener", StringComparison.OrdinalIgnoreCase))
            {
                element.RemoveAttribute("rel");
            }
        }

        return true;
    }

    /// <summary>Returns the replacement URL, or null to leave the original alone.</summary>
    private string? Resolve(string value, LinkTargets targets, MediaIndex media)
    {
        var url = value.Trim();

        // A known attachment always wins, whatever form it was written in.
        if (media.TryResolve(url, out var mediaUrl)) return mediaUrl;

        if (url.StartsWith('#') || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Uri? absolute = null;

        if (Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            // Another site entirely: leave it exactly as the author wrote it.
            if (!targets.SourceHosts.Contains(parsed.Host)) return null;
            absolute = parsed;
        }
        else if (!url.StartsWith('/'))
        {
            // A relative link inside the content; nothing to resolve.
            return null;
        }

        var path = absolute?.AbsolutePath ?? url.Split('?')[0].Split('#')[0];
        var query = absolute?.Query ?? QueryOf(url);
        var fragment = absolute?.Fragment ?? FragmentOf(url);

        // ?page_id=12 and ?p=34 are the pre-permalink forms and resolve to real content.
        if (query.Length > 0)
        {
            var id = IdFromQuery(query, "page_id");
            if (id is int pageId && targets.PagePaths.TryGetValue(pageId, out var pagePath))
            {
                return pagePath + fragment;
            }

            id = IdFromQuery(query, "p");
            if (id is int postId && targets.PostPermalinks.TryGetValue(postId, out var permalink))
            {
                return permalink + fragment;
            }

            if (query.Contains("page_id=") || query.Contains("p="))
            {
                logger.LogWarning("Could not resolve the WordPress link {Url}; leaving it as it was.", url);
                return null;
            }
        }

        // An attachment that was never imported would 404 as a relative path, so it keeps
        // pointing at the host that can still serve it.
        if (path.StartsWith("/wp-content/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/wp-includes/", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("No imported media matches {Url}; it still points at the old host.", url);
            return null;
        }

        var normalised = path.Length == 0 ? "/" : path.TrimEnd('/');
        if (normalised.Length == 0) normalised = "/";

        return normalised + query + fragment;
    }

    /// <summary>
    /// Matches an attachment URL against what was imported, allowing for the ways the same
    /// file gets written: a different scheme, a www prefix, or one of the resized copies
    /// WordPress derives, such as photo-300x150.png for photo.png.
    /// </summary>
    private sealed class MediaIndex
    {
        private readonly Dictionary<string, string> _exact;
        private readonly Dictionary<string, string> _byPath;

        public MediaIndex(IReadOnlyDictionary<string, string> mediaUrls)
        {
            _exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _byPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (source, local) in mediaUrls)
            {
                _exact[source] = local;

                var path = PathOf(source);
                if (path is not null) _byPath[path] = local;
            }
        }

        public bool TryResolve(string url, out string local)
        {
            if (_exact.TryGetValue(url, out local!)) return true;

            var path = PathOf(url);
            if (path is null)
            {
                local = string.Empty;
                return false;
            }

            if (_byPath.TryGetValue(path, out local!)) return true;

            // Fall back to the original the size variant was derived from.
            var original = SizeSuffix().Replace(path, "$1");
            if (original != path && _byPath.TryGetValue(original, out local!)) return true;

            local = string.Empty;
            return false;
        }

        /// <summary>Scheme and host removed, so http, https and www all collapse together.</summary>
        private static string? PathOf(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) return uri.AbsolutePath;
            return url.StartsWith('/') ? url.Split('?')[0].Split('#')[0] : null;
        }
    }

    /// <summary>Matches the -WIDTHxHEIGHT that WordPress appends to a resized copy.</summary>
    [GeneratedRegex(@"^(.*)-\d{1,5}x\d{1,5}(\.[A-Za-z0-9]+)$")]
    private static partial Regex SizeSuffixPattern();

    private static Regex SizeSuffix() => SizeSuffixPattern();

    private static string QueryOf(string url)
    {
        var index = url.IndexOf('?');
        if (index < 0) return string.Empty;

        var rest = url[index..];
        var hash = rest.IndexOf('#');
        return hash < 0 ? rest : rest[..hash];
    }

    private static string FragmentOf(string url)
    {
        var index = url.IndexOf('#');
        return index < 0 ? string.Empty : url[index..];
    }

    private static int? IdFromQuery(string query, string key)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length != 2) continue;
            if (!string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase)) continue;

            if (int.TryParse(parts[1], out var id)) return id;
        }

        return null;
    }
}
