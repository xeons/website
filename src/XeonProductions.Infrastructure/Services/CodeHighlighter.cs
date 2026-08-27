using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ColorCode;
using ColorCode.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace XeonProductions.Infrastructure.Services;

public interface ICodeHighlighter
{
    /// <summary>
    /// Highlights every pre/code block that declares a language, leaving the rest untouched.
    /// </summary>
    string Highlight(string? html);
}

/// <summary>
/// Server-side syntax highlighting.
///
/// Done on the server rather than in the browser so the markup arrives already coloured:
/// no highlighting library to ship, nothing for the content security policy to allow, and
/// the code reads correctly with JavaScript disabled. Stored content keeps the plain
/// pre/code form, so restyling later is a CSS change rather than a re-import.
/// </summary>
public class CodeHighlighter(IMemoryCache cache, ILogger<CodeHighlighter> logger) : ICodeHighlighter
{
    private readonly HtmlParser _parser = new();
    private readonly HtmlClassFormatter _formatter = new();

    // ColorCode ships no shell grammar, and shell is the most common language here.
    private static readonly ShellLanguage Shell = new();
    private static readonly object RegistrationGate = new();
    private static bool _registered;

    private static void EnsureLanguagesRegistered()
    {
        if (_registered) return;

        lock (RegistrationGate)
        {
            if (_registered) return;

            Languages.Load(Shell);
            _registered = true;
        }
    }

    public string Highlight(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        if (!html.Contains("<code", StringComparison.OrdinalIgnoreCase)) return html;

        EnsureLanguagesRegistered();

        var document = _parser.ParseDocument($"<body>{html}</body>");
        var changed = false;

        foreach (var code in document.QuerySelectorAll("pre > code").ToList())
        {
            var language = LanguageFrom(code);
            if (language is null) continue;

            // TextContent is the decoded source: entities are already resolved here.
            var source = code.TextContent;
            if (string.IsNullOrWhiteSpace(source)) continue;

            var highlighted = HighlightCached(source, language);
            if (highlighted is null) continue;

            code.InnerHtml = highlighted;
            changed = true;
        }

        return changed ? document.Body?.InnerHtml ?? html : html;
    }

    /// <summary>
    /// Highlighting the same block on every request is wasted work; the output depends only
    /// on the source and the language.
    /// </summary>
    private string? HighlightCached(string source, ILanguage language)
    {
        var key = $"hl:{language.Id}:{source.GetHashCode()}:{source.Length}";

        if (cache.TryGetValue(key, out string? cached)) return cached;

        string? result;

        try
        {
            var formatted = _formatter.GetHtmlString(source, language);
            result = ExtractInner(formatted);
        }
        catch (Exception ex)
        {
            // Bad input should show as plain code, never as a failed page.
            logger.LogWarning(ex, "Could not highlight a {Language} block.", language.Id);
            result = null;
        }

        cache.Set(key, result, TimeSpan.FromHours(6));
        return result;
    }

    /// <summary>
    /// ColorCode returns a complete pre/div wrapper. Only the inner markup is wanted, since
    /// the surrounding pre and code elements already exist in the content.
    /// </summary>
    private string? ExtractInner(string formatted)
    {
        if (string.IsNullOrWhiteSpace(formatted)) return null;

        var document = _parser.ParseDocument(formatted);

        var inner = document.QuerySelector("pre > code")
                    ?? document.QuerySelector("pre")
                    ?? (IElement?)document.Body;

        return inner?.InnerHtml;
    }

    private static ILanguage? LanguageFrom(IElement code)
    {
        var className = code.GetAttribute("class")
                        ?? code.ParentElement?.GetAttribute("class");

        if (string.IsNullOrWhiteSpace(className)) return null;

        var token = className
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(c => c.StartsWith("language-", StringComparison.OrdinalIgnoreCase)
                              || c.StartsWith("lang-", StringComparison.OrdinalIgnoreCase));

        if (token is null) return null;

        var name = token[(token.IndexOf('-') + 1)..].ToLowerInvariant();
        return Resolve(name);
    }

    private static ILanguage? Resolve(string name)
    {
        var id = name switch
        {
            "csharp" or "cs" or "c#" => LanguageId.CSharp,
            "cpp" or "c++" => LanguageId.Cpp,
            // ColorCode has no dedicated C grammar; the C++ one covers it closely enough.
            "c" => LanguageId.Cpp,
            "php" => LanguageId.Php,
            "javascript" or "js" => LanguageId.JavaScript,
            "typescript" or "ts" => LanguageId.TypeScript,
            "html" => LanguageId.Html,
            "xml" => LanguageId.Xml,
            "css" => LanguageId.Css,
            "sql" => LanguageId.Sql,
            "json" => LanguageId.Json,
            "python" or "py" => LanguageId.Python,
            "java" => LanguageId.Java,
            "powershell" or "ps" => LanguageId.PowerShell,
            "fsharp" or "fs" => LanguageId.FSharp,
            "vb" or "vbnet" => LanguageId.VbDotNet,
            "markdown" or "md" => LanguageId.Markdown,
            _ => null
        };

        if (id is not null) return Languages.FindById(id);

        return Shell.HasAlias(name) ? Shell : null;
    }
}
