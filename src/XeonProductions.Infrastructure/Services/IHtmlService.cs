namespace XeonProductions.Infrastructure.Services;

public interface IHtmlService
{
    string Sanitize(string? html);
    string ToPlainText(string? html);
    string BuildExcerpt(string? html, int maxChars = 220);
    int EstimateReadingMinutes(string? html);
    string AddHeadingAnchors(string html);
}
