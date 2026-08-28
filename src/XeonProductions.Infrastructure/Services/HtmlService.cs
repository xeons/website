using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace XeonProductions.Infrastructure.Services;

/// <summary>
/// Everything that touches author-supplied HTML funnels through here. Content is sanitised on
/// save rather than on render, so the hot path stays cheap.
/// </summary>
public partial class HtmlService : IHtmlService
{
    private readonly HtmlSanitizer _sanitizer;
    private readonly HtmlParser _parser = new();

    public HtmlService()
    {
        _sanitizer = new HtmlSanitizer();

        // The blog is code-heavy: allow the markup a syntax highlighter and embeds need.
        _sanitizer.AllowedTags.Add("figure");
        _sanitizer.AllowedTags.Add("figcaption");
        _sanitizer.AllowedTags.Add("iframe");
        _sanitizer.AllowedTags.Add("picture");
        _sanitizer.AllowedTags.Add("source");
        _sanitizer.AllowedTags.Add("details");
        _sanitizer.AllowedTags.Add("summary");
        _sanitizer.AllowedTags.Add("mark");

        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowedAttributes.Add("id");
        _sanitizer.AllowedAttributes.Add("loading");
        _sanitizer.AllowedAttributes.Add("srcset");
        _sanitizer.AllowedAttributes.Add("sizes");
        _sanitizer.AllowedAttributes.Add("allowfullscreen");
        _sanitizer.AllowedAttributes.Add("frameborder");
        _sanitizer.AllowedAttributes.Add("target");
        _sanitizer.AllowedAttributes.Add("rel");

        _sanitizer.AllowedSchemes.Add("mailto");

        // Only trusted embed hosts may appear in an iframe.
        _sanitizer.RemovingAttribute += (_, e) =>
        {
            if (!string.Equals(e.Tag.NodeName, "IFRAME", StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(e.Attribute.Name, "src", StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = IsAllowedEmbed(e.Attribute.Value);
        };
    }

    private static readonly string[] EmbedHosts =
    [
        "www.youtube.com", "youtube.com", "www.youtube-nocookie.com",
        "player.vimeo.com", "gist.github.com", "codepen.io", "open.spotify.com"
    ];

    private static bool IsAllowedEmbed(string? src) =>
        Uri.TryCreate(src, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && EmbedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    public string Sanitize(string? html) =>
        string.IsNullOrWhiteSpace(html) ? string.Empty : StripEditorArtifacts(_sanitizer.Sanitize(html));

    /// <summary>
    /// Class names the WordPress editor emitted that mean nothing here.
    ///
    /// Only the ones with no effect are listed. Alignment, the image and table block
    /// wrappers, the rounded variation and caption classes all still describe how an entry
    /// was laid out and are implemented in the stylesheet, so they stay.
    /// </summary>
    private static readonly HashSet<string> EditorNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        "wp-block-heading", "wp-block-list", "wp-block-paragraph", "wp-block-quote",
        "wp-block-code", "wp-block-preformatted", "wp-block-separator", "wp-block-group",
        "size-full", "size-large", "size-medium", "size-thumbnail", "is-style-default"
    };

    /// <summary>
    /// Rewrites class attributes, dropping names that carried meaning only inside WordPress.
    ///
    /// Walks the document rather than matching text. A code sample showing WordPress markup
    /// arrives entity encoded, but the sanitiser has already decoded it into a text node by
    /// this point, so a textual rewrite would reach inside samples and edit them. Only real
    /// attributes are touched here. The markup is returned untouched when nothing matched,
    /// so content without artifacts is never reserialised.
    /// </summary>
    private string StripEditorArtifacts(string html)
    {
        if (!html.Contains("class=", StringComparison.OrdinalIgnoreCase)) return html;

        var document = _parser.ParseDocument($"<body>{html}</body>");
        if (document.Body is null) return html;

        var changed = false;

        foreach (var element in document.Body.QuerySelectorAll("[class]").ToList())
        {
            var kept = element.ClassList.Where(Meaningful).ToArray();
            if (kept.Length == element.ClassList.Length) continue;

            changed = true;

            if (kept.Length == 0)
            {
                element.RemoveAttribute("class");
            }
            else
            {
                element.ClassName = string.Join(' ', kept);
            }
        }

        return changed ? document.Body.InnerHtml : html;
    }

    /// <summary>An attachment id names a row in a database that is no longer consulted.</summary>
    private static bool Meaningful(string token) =>
        !EditorNoise.Contains(token) && !AttachmentId().IsMatch(token);

    public string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // Drop script/style bodies entirely before stripping tags, or their source leaks in.
        var text = ScriptOrStyle().Replace(html, " ");
        text = BlockBreak().Replace(text, "\n");
        text = Tags().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        return Whitespace().Replace(text, " ").Trim();
    }

    public string BuildExcerpt(string? html, int maxChars = 220)
    {
        var text = ToPlainText(html);
        if (text.Length <= maxChars) return text;

        // Cut on a word boundary so the ellipsis does not land mid-word.
        var cut = text.LastIndexOf(' ', Math.Min(maxChars, text.Length - 1));
        if (cut < maxChars / 2) cut = maxChars;
        return string.Concat(text.AsSpan(0, cut).TrimEnd(",.;: ".AsSpan()), "...");
    }

    public int EstimateReadingMinutes(string? html)
    {
        var words = ToPlainText(html).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Max(1, (int)Math.Ceiling(words / 220.0));
    }

    /// <summary>
    /// Gives h2/h3 headings stable ids so the table of contents and deep links work.
    /// </summary>
    public string AddHeadingAnchors(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return Headings().Replace(html, m =>
        {
            var level = m.Groups["level"].Value;
            var attrs = m.Groups["attrs"].Value;
            var inner = m.Groups["inner"].Value;

            if (attrs.Contains("id=", StringComparison.OrdinalIgnoreCase)) return m.Value;

            var slug = SlugHelper.Slugify(ToPlainText(inner));
            if (string.IsNullOrEmpty(slug)) return m.Value;

            var candidate = slug;
            for (var i = 2; !used.Add(candidate); i++) candidate = $"{slug}-{i}";

            return $"<h{level}{attrs} id=\"{candidate}\">{inner}</h{level}>";
        });
    }

    [GeneratedRegex("^wp-image-[0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex AttachmentId();

    [GeneratedRegex(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"</(p|div|br|li|h[1-6]|tr|pre)\s*>|<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBreak();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<h(?<level>[23])(?<attrs>[^>]*)>(?<inner>.*?)</h\k<level>>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Headings();
}
