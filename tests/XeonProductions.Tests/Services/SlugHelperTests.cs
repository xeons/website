using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class SlugHelperTests
{
    [Theory]
    [InlineData("Hello World", "hello-world")]
    [InlineData("  Trim  Me  ", "trim-me")]
    [InlineData("Multiple---Dashes", "multiple-dashes")]
    [InlineData("Punctuation! Everywhere?", "punctuation-everywhere")]
    [InlineData("UPPER CASE", "upper-case")]
    [InlineData("already-a-slug", "already-a-slug")]
    public void TitlesBecomeUrlSegments(string input, string expected) =>
        Assert.Equal(expected, SlugHelper.Slugify(input));

    /// <summary>
    /// These would otherwise collapse onto the same slug as plain "C", which the snippets
    /// pages depend on keeping apart.
    /// </summary>
    [Theory]
    [InlineData("C#", "c-sharp")]
    [InlineData("C++", "c-plus-plus")]
    [InlineData(".NET", "dotnet")]
    [InlineData("C", "c")]
    public void LanguageNamesStayDistinct(string input, string expected) =>
        Assert.Equal(expected, SlugHelper.Slugify(input));

    [Theory]
    [InlineData("Creme Brulee", "creme-brulee")]
    [InlineData("Zurich", "zurich")]
    public void AsciiTitlesAreUnchanged(string input, string expected) =>
        Assert.Equal(expected, SlugHelper.Slugify(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void UnusableInputYieldsAnEmptySlug(string? input) =>
        Assert.Equal(string.Empty, SlugHelper.Slugify(input));

    [Fact]
    public void LongTitlesAreTruncatedToTheLimit()
    {
        var slug = SlugHelper.Slugify(string.Join(" ", Enumerable.Repeat("word", 100)), maxLength: 50);

        Assert.True(slug.Length <= 50);
        Assert.DoesNotContain("--", slug);
        Assert.False(slug.EndsWith('-'));
    }

    [Fact]
    public async Task AFreeSlugIsUsedAsIs() =>
        Assert.Equal("post", await SlugHelper.MakeUniqueAsync("post", _ => Task.FromResult(false)));

    [Fact]
    public async Task ATakenSlugGainsASuffix()
    {
        var taken = new HashSet<string> { "post", "post-2" };

        var slug = await SlugHelper.MakeUniqueAsync("post", c => Task.FromResult(taken.Contains(c)));

        Assert.Equal("post-3", slug);
    }

    [Fact]
    public async Task AnEmptyBaseSlugFallsBackToUntitled() =>
        Assert.Equal("untitled", await SlugHelper.MakeUniqueAsync("", _ => Task.FromResult(false)));
}
