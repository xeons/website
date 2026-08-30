using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

/// <summary>
/// A variant's path is derived, not recorded. The code that writes the file and the code that
/// offers its URL both call this, so a disagreement between them would not be a wrong name in
/// one place: it would be a srcset pointing at files that are not there.
/// </summary>
public class MediaVariantPathTests
{
    [Theory]
    [InlineData("2026/08/logo-light.png", 800, "2026/08/logo-light-800w.webp")]
    [InlineData("2026/08/logo-light.png", 1345, "2026/08/logo-light-1345w.webp")]
    [InlineData("2026/01/a.jpeg", 480, "2026/01/a-480w.webp")]
    public void AVariantSitsBesideTheOriginalUnderItsWidth(string original, int width, string expected) =>
        Assert.Equal(expected, MediaService.VariantPath(original, width));

    /// <summary>Stored paths use forward slashes whatever the platform wrote them on.</summary>
    [Fact]
    public void TheSeparatorIsAlwaysAForwardSlash() =>
        Assert.Equal("2026/08/a-800w.webp", MediaService.VariantPath("2026\\08\\a.png", 800));

    /// <summary>
    /// A name carrying dots keeps all but the extension, or two uploads differing only after
    /// the first dot would land on one file.
    /// </summary>
    [Fact]
    public void OnlyTheExtensionIsReplaced() =>
        Assert.Equal("2026/08/my.logo.v2-800w.webp", MediaService.VariantPath("2026/08/my.logo.v2.png", 800));

    /// <summary>The thumbnail's own name must never collide with a variant's.</summary>
    [Fact]
    public void AVariantIsNamedApartFromTheThumbnail() =>
        Assert.DoesNotContain("-thumb", MediaService.VariantPath("2026/08/logo.png", 480));
}
