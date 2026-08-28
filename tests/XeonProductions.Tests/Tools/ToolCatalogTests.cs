using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using XeonProductions.Web.Tools;

namespace XeonProductions.Tests.Tools;

public class ToolCatalogTests
{
    public static TheoryData<string> Slugs()
    {
        var data = new TheoryData<string>();

        foreach (var tool in ToolCatalog.All) data.Add(tool.Slug);

        return data;
    }

    [Fact]
    public void TheCatalogIsNotEmpty() => Assert.NotEmpty(ToolCatalog.All);

    /// <summary>Two tools sharing a slug would make one of them unreachable.</summary>
    [Fact]
    public void EverySlugIsUnique()
    {
        var duplicates = ToolCatalog.All
            .GroupBy(t => t.Slug, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void ASlugIsSafeInAUrl(string slug) =>
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug);

    [Fact]
    public void EveryToolCarriesTheTextItsPageNeeds()
    {
        foreach (var tool in ToolCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Name), $"{tool.Slug} has no name.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Summary), $"{tool.Slug} has no summary.");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{tool.Slug} has no description.");
        }
    }

    /// <summary>The type is rendered by DynamicComponent, which needs a component.</summary>
    [Fact]
    public void EveryToolPointsAtAComponent()
    {
        foreach (var tool in ToolCatalog.All)
        {
            Assert.True(
                typeof(IComponent).IsAssignableFrom(tool.ComponentType),
                $"{tool.Slug} points at {tool.ComponentType.Name}, which is not a component.");
        }
    }

    [Fact]
    public void NoComponentIsUsedTwice()
    {
        var duplicates = ToolCatalog.All
            .GroupBy(t => t.ComponentType)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [MemberData(nameof(Slugs))]
    public void FindLocatesEveryTool(string slug) => Assert.NotNull(ToolCatalog.Find(slug));

    /// <summary>A visitor typing the address by hand should not be punished for capitals.</summary>
    [Fact]
    public void FindIgnoresCase()
    {
        var first = ToolCatalog.All[0];

        Assert.Same(first, ToolCatalog.Find(first.Slug.ToUpperInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-tool")]
    public void FindReturnsNothingForAnUnknownSlug(string? slug) =>
        Assert.Null(ToolCatalog.Find(slug));

    [Fact]
    public void EveryCategoryHoldsAtLeastOneTool()
    {
        foreach (var category in Enum.GetValues<ToolCategory>())
        {
            Assert.True(
                ToolCatalog.InCategory(category).Any(),
                $"{category} has no tools, so its heading would render empty.");
        }
    }

    /// <summary>Two categories sharing a heading would read as one section split in two.</summary>
    [Fact]
    public void EveryCategoryHasItsOwnHeading()
    {
        var headings = Enum.GetValues<ToolCategory>()
            .Select(ToolCatalog.HeadingFor)
            .ToList();

        Assert.DoesNotContain(headings, string.IsNullOrWhiteSpace);
        Assert.Equal(headings.Count, headings.Distinct().Count());
    }

    /// <summary>
    /// The behaviour of every tool lives in tools.js, keyed by the name its markup carries in
    /// data-tool. Nothing ties the catalog to that file, so a tool added here with no handler
    /// there renders a dead panel. This walks the components' markup and checks each name is
    /// one the script knows.
    /// </summary>
    [Fact]
    public void EveryToolPanelHasAHandlerInTheScript()
    {
        var script = File.ReadAllText(RepositoryFile("src/XeonProductions.Web/wwwroot/js/tools.js"));
        var missing = new List<string>();

        foreach (var tool in ToolCatalog.All)
        {
            var markup = File.ReadAllText(ComponentFile(tool.ComponentType));
            var name = Regex.Match(markup, "data-tool=\"([a-z0-9-]+)\"").Groups[1].Value;

            Assert.False(string.IsNullOrEmpty(name),
                $"{tool.ComponentType.Name} renders no data-tool attribute.");

            // Matches both the bare and the quoted key forms a JavaScript object literal allows.
            if (!Regex.IsMatch(script, $@"(^|[\s{{,])'?{Regex.Escape(name)}'?\s*:", RegexOptions.Multiline))
            {
                missing.Add($"{tool.Slug} renders data-tool=\"{name}\" with no handler in tools.js");
            }
        }

        Assert.Empty(missing);
    }

    private static string ComponentFile(Type component) =>
        RepositoryFile($"src/XeonProductions.Web/Components/Tools/{component.Name}.razor");

    /// <summary>
    /// Tests run from the build output, so the repository root is found by walking up for the
    /// solution file rather than assumed.
    /// </summary>
    private static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XeonProductions.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var path = Path.Combine(directory!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(path), $"Expected to find {relativePath}.");

        return path;
    }
}
