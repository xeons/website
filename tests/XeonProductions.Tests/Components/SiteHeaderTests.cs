using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Components.Layout;

namespace XeonProductions.Tests.Components;

/// <summary>
/// The logo is offered at two sizes and the browser picks one against the sizes attribute,
/// not against the layout it ends up in. Understating that width costs nothing that fails:
/// the small candidate is fetched, upscaled, and merely looks soft.
/// </summary>
public class SiteHeaderTests : BunitContext
{
    // Proportions of the real logo: far wider than it is tall, and larger than the thumbnail.
    private const int LogoWidth = 1345;
    private const int LogoHeight = 318;
    private const int ThumbnailWidth = 480;

    public SiteHeaderTests()
    {
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var navigation = new Mock<INavigationService>();
        navigation.Setup(n => n.GetMenuAsync(It.IsAny<MenuLocation>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var media = new Mock<IMediaService>();
        media.Setup(m => m.ResolveByUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MediaVariants(
                "/media/logo.png", LogoWidth, LogoHeight, "/media/logo-thumb.webp", ThumbnailWidth,
                [
                    new MediaVariant("/media/logo-thumb.webp", ThumbnailWidth),
                    new MediaVariant("/media/logo-800w.webp", 800),
                    new MediaVariant($"/media/logo-{LogoWidth}w.webp", LogoWidth)
                ]));

        Services.AddSingleton(navigation.Object);
        Services.AddSingleton(media.Object);
    }

    private static SiteSettings Settings(HeaderLayout layout) => new()
    {
        SiteTitle = "Xeon Productions",
        LogoUrl = "/media/logo.png",
        HeaderLayout = layout,
        LogoMaxHeight = 96,
        ContentWidth = 1200
    };

    private IRenderedComponent<SiteHeader> RenderWith(HeaderLayout layout) =>
        Render<SiteHeader>(p => p.Add(h => h.Settings, Settings(layout)));

    /// <summary>The attribute lives on the source now, which is what offers the WebP copies.</summary>
    private string SizesFor(HeaderLayout layout) =>
        RenderWith(layout).Find("source[type=\"image/webp\"]").GetAttribute("sizes")!;

    /// <summary>
    /// A srcset carrying both formats would give a browser without WebP no way to decline it.
    /// Every WebP belongs on the source; the img beneath keeps the original alone.
    /// </summary>
    [Fact]
    public void TheWebpCopiesAreOfferedOnlyThroughTheSource()
    {
        var header = RenderWith(HeaderLayout.Banner);

        var source = header.Find("source[type=\"image/webp\"]").GetAttribute("srcset")!;
        var image = header.Find("img.logo-light");

        Assert.Contains(".webp 480w", source);
        Assert.Contains($".webp {LogoWidth}w", source);

        Assert.Equal("/media/logo.png", image.GetAttribute("src"));
        Assert.Null(image.GetAttribute("srcset"));
    }

    /// <summary>
    /// The banner layout sets max-height to none and the width to 100%, so the logo lands at
    /// the container's width. A figure derived from the height would be a third of that, and
    /// the thumbnail would win a slot it cannot fill.
    /// </summary>
    [Fact]
    public void TheBannerLayoutAsksForTheContainerWidth()
    {
        var sizes = SizesFor(HeaderLayout.Banner);

        Assert.Contains("1200px", sizes);
        Assert.DoesNotContain("406px", sizes);
    }

    [Theory]
    [InlineData(HeaderLayout.LogoLeft)]
    [InlineData(HeaderLayout.Centered)]
    public void TheOtherLayoutsAskForTheWidthTheHeightImplies(HeaderLayout layout)
    {
        // 96px tall at these proportions is 406px wide.
        Assert.Contains("406px", SizesFor(layout));
    }

    /// <summary>Whatever the layout, the declared width has to be one the thumbnail cannot
    /// satisfy on its own, or the browser never reaches for the full image.</summary>
    [Fact]
    public void TheBannerAsksForMoreThanTheThumbnailHolds()
    {
        var sizes = SizesFor(HeaderLayout.Banner);
        var declared = int.Parse(sizes.Split(", ")[^1].Replace("px", ""));

        Assert.True(declared > ThumbnailWidth,
            $"declared {declared}px, so the {ThumbnailWidth}w thumbnail would be chosen and upscaled.");
    }
}
