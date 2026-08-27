namespace XeonProductions.Infrastructure.Services;

public interface IImportLinkRewriter
{
    string Rewrite(string? html, LinkTargets targets);
}
