using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using XeonProductions.Domain.Enums;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Components.Pages;
using XeonProductions.Web.Tools;

namespace XeonProductions.Tests.Components;

/// <summary>
/// The tool pages are routed by slug through DynamicComponent, so a component that fails to
/// render only shows up when someone opens that one address. These render every tool in the
/// catalog rather than a sample.
/// </summary>
public class ToolPageTests : BunitContext
{
    public ToolPageTests()
    {
        Services.AddLogging();

        var settings = new Mock<ISiteSettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteSettings { SiteTitle = "Xeon Productions", SiteUrl = "https://example.com" });

        // The not-found body falls back to the default sidebar, which asks for its widgets.
        var navigation = new Mock<INavigationService>();
        navigation.Setup(n => n.GetWidgetsAsync(It.IsAny<WidgetArea>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        Services.AddSingleton(settings.Object);
        Services.AddSingleton(navigation.Object);
        Services.AddSingleton(new Mock<IHttpContextAccessor>().Object);
        Services.AddSingleton(new ResourceAssetCollection([]));
    }

    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string>();

        foreach (var tool in ToolCatalog.All) data.Add(tool.Slug);

        return data;
    }

    private IRenderedComponent<ToolPage> RenderTool(string? slug) =>
        Render<ToolPage>(parameters => parameters.Add(p => p.Slug, slug));

    [Theory]
    [MemberData(nameof(Slugs))]
    public void EveryToolRenders(string slug)
    {
        var page = RenderTool(slug);
        var tool = ToolCatalog.Find(slug)!;

        Assert.Contains(tool.Name, page.Find("h1").TextContent);
    }

    /// <summary>Without the panel attribute the script has nothing to bind the tool to.</summary>
    [Theory]
    [MemberData(nameof(Slugs))]
    public void EveryToolRendersItsPanel(string slug)
    {
        var page = RenderTool(slug);

        Assert.NotNull(page.Find("[data-tool]"));
    }

    /// <summary>
    /// A control the script reads options from is useless if it carries no name, and the
    /// mistake is invisible until the option silently stops applying.
    /// </summary>
    [Theory]
    [MemberData(nameof(Slugs))]
    public void EveryOptionControlIsNamed(string slug)
    {
        var page = RenderTool(slug);

        foreach (var control in page.FindAll("[data-tool-option]"))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(control.GetAttribute("data-tool-option")),
                $"{slug} has an option control with an empty name.");
        }
    }

    /// <summary>Every labelled control needs the id its label points at.</summary>
    [Theory]
    [MemberData(nameof(Slugs))]
    public void EveryLabelPointsAtSomething(string slug)
    {
        var page = RenderTool(slug);

        foreach (var label in page.FindAll("label[for]"))
        {
            var target = label.GetAttribute("for");

            Assert.False(string.IsNullOrWhiteSpace(target));
            Assert.NotNull(page.Find($"#{target}"));
        }
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void EveryToolLinksBackToTheIndex(string slug)
    {
        var page = RenderTool(slug);

        Assert.Contains(
            page.FindAll("a"),
            a => a.GetAttribute("href") == "/tools");
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void EveryToolSaysNothingIsSent(string slug)
    {
        var page = RenderTool(slug);

        Assert.Contains("This runs entirely in your browser.", page.Markup);
    }

    /// <summary>
    /// An unknown slug hands off to the router rather than rendering an apology, so the
    /// response carries a real 404 instead of a 200 that a crawler would index.
    /// </summary>
    [Theory]
    [InlineData("not-a-tool")]
    [InlineData("")]
    public void AnUnknownSlugRendersNothingAndAsksForNotFound(string slug)
    {
        var page = RenderTool(slug);

        Assert.True(string.IsNullOrWhiteSpace(page.Markup),
            $"Expected no markup for '{slug}', got: {page.Markup}");
    }

    [Fact]
    public void TheIndexListsEveryTool()
    {
        var page = Render<ToolsIndex>();
        var links = page.FindAll("a.tool-card")
            .Select(a => a.GetAttribute("href"))
            .ToList();

        foreach (var tool in ToolCatalog.All)
        {
            Assert.Contains($"/tools/{tool.Slug}", links);
        }
    }

    [Fact]
    public void TheIndexShowsEveryCategoryHeading()
    {
        var page = Render<ToolsIndex>();
        var headings = page.FindAll("h2").Select(h => h.TextContent).ToList();

        foreach (var category in Enum.GetValues<ToolCategory>())
        {
            Assert.Contains(ToolCatalog.HeadingFor(category), headings);
        }
    }
}
