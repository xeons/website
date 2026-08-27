using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;

namespace XeonProductions.Infrastructure.Services;

public interface IWordPressMarkupCleaner
{
    string Clean(string? html);
    int LastCodeBlockCount { get; }
}

/// <summary>
/// Rewrites plugin-specific markup into plain semantic HTML during import.
///
/// The snippets pages were built with Crayon, later renamed Urvanov, a WordPress syntax
/// highlighter. It renders a code sample as a table of per-token spans wrapped in a toolbar,
/// which is meaningless without the plugin's own CSS and JavaScript. Fortunately it also
/// stores the untouched source in a hidden textarea, so the original code can be recovered
/// exactly and re-emitted as a pre/code pair that any highlighter can work with.
/// </summary>
public class WordPressMarkupCleaner(ILogger<WordPressMarkupCleaner> logger) : IWordPressMarkupCleaner
{
    private readonly HtmlParser _parser = new();

    public int LastCodeBlockCount { get; private set; }

    public string Clean(string? html)
    {
        LastCodeBlockCount = 0;

        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        // Nothing plugin-shaped in here, so leave the markup exactly as it was.
        if (!html.Contains("crayon", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("urvanov", StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        var document = _parser.ParseDocument($"<body>{html}</body>");

        foreach (var block in FindHighlighterBlocks(document))
        {
            var code = ExtractSource(block);

            if (code is null)
            {
                logger.LogWarning("Found a highlighter block with no recoverable source; leaving it in place.");
                continue;
            }

            var language = ExtractLanguage(block);
            block.Replace(BuildCodeBlock(document, code, language));

            LastCodeBlockCount++;
        }

        // Strip any orphaned plugin chrome the wrapper search did not cover.
        foreach (var leftover in document.QuerySelectorAll(
                     ".crayon-button, .urvanov-syntax-highlighter-toolbar, .crayon-toolbar").ToList())
        {
            leftover.Remove();
        }

        return document.Body?.InnerHtml ?? html;
    }

    /// <summary>
    /// The outermost element of each highlighter instance. Selecting the wrapper rather than
    /// the inner table means the toolbar and line numbers go with it.
    /// </summary>
    private static List<IElement> FindHighlighterBlocks(IDocument document)
    {
        var candidates = document
            .QuerySelectorAll("[class*='urvanov-syntax-highlighter'], [class*='crayon-syntax'], [id^='crayon-']")
            .ToList();

        // Keep only the outermost: a nested match would be replaced twice.
        return candidates
            .Where(element => !candidates.Any(other => other != element && other.Contains(element)))
            .ToList();
    }

    /// <summary>The plugin keeps the unhighlighted source in a textarea for its copy button.</summary>
    private static string? ExtractSource(IElement block)
    {
        var textarea = block.QuerySelector("textarea");

        if (textarea is not null)
        {
            var raw = (textarea as IHtmlTextAreaElement)?.TextContent ?? textarea.TextContent;
            if (!string.IsNullOrWhiteSpace(raw)) return Normalise(raw);
        }

        // Fall back to reassembling the rendered lines, one element per source line.
        var lines = block.QuerySelectorAll(".crayon-line, .urvanov-syntax-highlighter-line").ToList();
        if (lines.Count == 0) return null;

        return Normalise(string.Join('\n', lines.Select(line => line.TextContent)));
    }

    private static string Normalise(string source) =>
        source.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');

    private static string? ExtractLanguage(IElement block)
    {
        var label = block.QuerySelector(".crayon-language, .urvanov-syntax-highlighter-language")
            ?.TextContent?.Trim();

        return MapLanguage(label);
    }

    /// <summary>Maps the plugin's display names onto the identifiers used on pre/code.</summary>
    public static string? MapLanguage(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        return label.Trim().ToLowerInvariant() switch
        {
            "c#" or "csharp" or "c-sharp" => "csharp",
            "c" => "c",
            "c++" or "cpp" => "cpp",
            "php" => "php",
            "shell" or "bash" or "sh" or "console" => "bash",
            "js" or "javascript" => "javascript",
            "ts" or "typescript" => "typescript",
            "html" or "xhtml" => "html",
            "xml" => "xml",
            "css" => "css",
            "sql" or "mysql" => "sql",
            "json" => "json",
            "python" or "py" => "python",
            "java" => "java",
            "powershell" or "ps" => "powershell",
            _ => null
        };
    }

    private static INode BuildCodeBlock(IDocument document, string code, string? language)
    {
        var pre = document.CreateElement("pre");
        var codeElement = document.CreateElement("code");

        if (!string.IsNullOrEmpty(language))
        {
            codeElement.SetAttribute("class", $"language-{language}");
        }

        // Assigning TextContent escapes the source, so angle brackets in the code survive
        // as characters rather than becoming markup.
        codeElement.TextContent = code;

        pre.AppendChild(codeElement);
        return pre;
    }
}
