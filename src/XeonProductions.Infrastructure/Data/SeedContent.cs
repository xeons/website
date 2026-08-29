namespace XeonProductions.Infrastructure.Data;

/// <summary>
/// Body text given to the pages a fresh install creates. Those pages stand in for entries a
/// site is expected to have, so anything still carrying this text is a placeholder rather
/// than content, and the WordPress importer writes over it.
/// </summary>
public static class SeedContent
{
    public const string AboutHtml =
        "<p>Replace this from the admin, or run the WordPress importer.</p>";

    public const string ContactHtml =
        "<p>Questions, corrections or work enquiries are all welcome.</p>";

    /// <summary>True while the content is still exactly what the seeder wrote.</summary>
    public static bool IsPlaceholder(string? html) => html is AboutHtml or ContactHtml;
}
