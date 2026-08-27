namespace XeonProductions.Infrastructure.Services;

public interface IContentRenderer
{
    /// <summary>
    /// Turns stored content into display-ready HTML: code blocks highlighted and headings
    /// given anchors. Stored content stays plain, so this can change without a re-import.
    /// </summary>
    string Render(string? contentHtml);
}

public class ContentRenderer(IHtmlService html, ICodeHighlighter highlighter) : IContentRenderer
{
    public string Render(string? contentHtml)
    {
        if (string.IsNullOrWhiteSpace(contentHtml)) return string.Empty;

        // Highlight first: adding heading ids does not disturb code, but rewriting code
        // after the anchors are in place would mean parsing the document twice over.
        var highlighted = highlighter.Highlight(contentHtml);

        return html.AddHeadingAnchors(highlighted);
    }
}
