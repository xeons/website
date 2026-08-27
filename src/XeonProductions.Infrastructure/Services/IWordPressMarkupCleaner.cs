namespace XeonProductions.Infrastructure.Services;

public interface IWordPressMarkupCleaner
{
    string Clean(string? html);
    int LastCodeBlockCount { get; }
}
