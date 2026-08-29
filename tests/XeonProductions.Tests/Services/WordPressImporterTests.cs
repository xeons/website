using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class WordPressImporterTests
{
    [Fact]
    public void KeepsContentThatIsAlreadyImported()
    {
        var replace = WordPressImporter.ShouldReplaceExisting(
            existingHtml: "<p>Imported once already.</p>",
            incomingHtml: "<p>Something else.</p>",
            overwrite: false);

        Assert.False(replace);
    }

    [Fact]
    public void ReplacesContentWhenOverwriteIsAsked()
    {
        var replace = WordPressImporter.ShouldReplaceExisting(
            existingHtml: "<p>Imported once already.</p>",
            incomingHtml: "<p>Something else.</p>",
            overwrite: true);

        Assert.True(replace);
    }

    /// <summary>
    /// The seeder runs at startup and creates these pages, so on a fresh install the importer
    /// meets them before it reaches the real entries. Skipping them leaves the site showing
    /// placeholder text with nothing to say the import missed anything.
    /// </summary>
    [Theory]
    [InlineData(SeedContent.AboutHtml)]
    [InlineData(SeedContent.ContactHtml)]
    public void ReplacesAPageTheSeederCreated(string seeded)
    {
        var replace = WordPressImporter.ShouldReplaceExisting(
            existingHtml: seeded,
            incomingHtml: "<h2>About this site</h2><p>I started this site in 2003.</p>",
            overwrite: false);

        Assert.True(replace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void KeepsThePlaceholderWhenTheSourceIsEmpty(string? incoming)
    {
        var replace = WordPressImporter.ShouldReplaceExisting(
            existingHtml: SeedContent.ContactHtml,
            incomingHtml: incoming,
            overwrite: false);

        Assert.False(replace);
    }

    [Fact]
    public void TreatsAnEditedPlaceholderAsContent()
    {
        var edited = SeedContent.AboutHtml + "<p>Written by hand in the admin.</p>";

        var replace = WordPressImporter.ShouldReplaceExisting(
            existingHtml: edited,
            incomingHtml: "<p>From WordPress.</p>",
            overwrite: false);

        Assert.False(replace);
    }
}
