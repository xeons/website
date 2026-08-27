namespace XeonProductions.Infrastructure.Services;

public interface ICodeHighlighter
{
    /// <summary>
    /// Highlights every pre/code block that declares a language, leaving the rest untouched.
    /// </summary>
    string Highlight(string? html);
}
