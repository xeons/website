using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using XeonProductions.Domain.Entities;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Components.Pages;

namespace XeonProductions.Tests.Components;

/// <summary>
/// The child page list is a conditional block, so only a render reaches it.
/// </summary>
public class ContentPageTests : BunitContext
{
    private readonly Mock<IContentService> _content = new();

    public ContentPageTests()
    {
        Services.AddLogging();

        var settings = new Mock<ISiteSettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteSettings { SiteTitle = "Xeon Productions", SiteUrl = "https://example.com" });

        var navigation = new Mock<INavigationService>();
        navigation.Setup(n => n.GetWidgetsAsync(It.IsAny<WidgetArea>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var renderer = new Mock<IContentRenderer>();
        renderer.Setup(r => r.Render(It.IsAny<string>())).Returns((string? html) => html ?? string.Empty);

        Services.AddSingleton(_content.Object);
        Services.AddSingleton(renderer.Object);
        Services.AddSingleton(new Mock<IMediaService>().Object);
        Services.AddSingleton(settings.Object);
        Services.AddSingleton(navigation.Object);
        Services.AddSingleton(new Mock<IHttpContextAccessor>().Object);
        Services.AddSingleton(new ResourceAssetCollection([]));
    }

    /// <summary>A parent that already links its own children, which is the usual shape.</summary>
    private static Page ParentWithChildren(bool showChildLinks) => new()
    {
        Id = 1,
        Slug = "reviews",
        Title = "Reviews",
        Status = ContentStatus.Published,
        ContentHtml = "<p><a href=\"/reviews/starfield-review\">Starfield</a></p>",
        ShowChildLinks = showChildLinks,
        Children =
        [
            new Page { Id = 2, Slug = "starfield-review", Title = "Starfield Review" },
            new Page { Id = 3, Slug = "high-on-life-review", Title = "High on Life Review" }
        ]
    };

    private IRenderedComponent<ContentPage> RenderPage(Page page)
    {
        _content.Setup(c => c.GetPageByPathAsync("reviews", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        return Render<ContentPage>(p => p.Add(c => c.Path, "reviews"));
    }

    [Fact]
    public void ListsChildPagesWhenThePageAsksFor()
    {
        var markup = RenderPage(ParentWithChildren(showChildLinks: true)).Markup;

        Assert.Contains("In this section", markup);
        Assert.Contains("/reviews/high-on-life-review", markup);
    }

    /// <summary>
    /// Every page here that has children introduces them in its own content, so the list is
    /// the same links twice over. Turning it off has to remove the whole section, heading
    /// included, rather than leaving an empty one behind.
    /// </summary>
    [Fact]
    public void LeavesOutTheChildListWhenThePageDoesNotAskForIt()
    {
        var markup = RenderPage(ParentWithChildren(showChildLinks: false)).Markup;

        Assert.DoesNotContain("In this section", markup);
        Assert.DoesNotContain("/reviews/high-on-life-review", markup);

        // The content's own link to a child is not the automatic list and stays.
        Assert.Contains("/reviews/starfield-review", markup);
    }
}
