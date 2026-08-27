namespace XeonProductions.Infrastructure.Services;

public interface IContentRenderer
{
    /// <summary>
    /// Turns stored content into display-ready HTML: code blocks highlighted and headings
    /// given anchors. Stored content stays plain, so this can change without a re-import.
    /// </summary>
    string Render(string? contentHtml);
}
