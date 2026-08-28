using Microsoft.Extensions.Logging.Abstractions;
using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class WordPressMarkupCleanerTests
{
    private static WordPressMarkupCleaner Cleaner() =>
        new(NullLogger<WordPressMarkupCleaner>.Instance);

    private const string ContactForm7 = """
        <p>
        </p><div class="wpcf7 no-js" id="wpcf7-f22-o1" lang="en-US" dir="ltr">
        <div class="screen-reader-response"><p></p> <ul></ul></div>
        <form action="/wp-json/wp/v2/pages?status=publish" method="post" class="wpcf7-form init">
        <p>Name (required)<br>
        <span class="wpcf7-form-control-wrap"><input type="text" name="your-name"></span>
        </p>
        <p><input class="wpcf7-submit" type="submit" value="Send"></p>
        </form>
        </div>
        <p></p>
        """;

    [Fact]
    public void AnImportedContactFormIsRemoved()
    {
        var result = Cleaner().Clean(ContactForm7);

        Assert.DoesNotContain("wpcf7", result);
        Assert.DoesNotContain("<form", result);
        Assert.DoesNotContain("your-name", result);
    }

    /// <summary>The plugin's own endpoint does not exist here, so a surviving action is a bug.</summary>
    [Fact]
    public void TheDeadEndpointDoesNotSurvive()
    {
        var result = Cleaner().Clean(ContactForm7);

        Assert.DoesNotContain("wp-json", result);
    }

    [Fact]
    public void TheEmptyParagraphsAroundItGoTo()
    {
        var result = Cleaner().Clean(ContactForm7);

        Assert.DoesNotContain("<p>", result);
        Assert.True(string.IsNullOrWhiteSpace(result), $"expected nothing left, got: {result}");
    }

    [Fact]
    public void ProseAroundTheFormIsKept()
    {
        var html = "<p>Drop me a line.</p>" + ContactForm7 + "<p>Or find me elsewhere.</p>";

        var result = Cleaner().Clean(html);

        Assert.Contains("Drop me a line.", result);
        Assert.Contains("Or find me elsewhere.", result);
        Assert.DoesNotContain("wpcf7", result);
    }

    [Fact]
    public void ContentWithNoPluginMarkupIsUntouched()
    {
        const string html = "<p>Just a paragraph.</p><figure class=\"alignleft\"><img src=\"/a.png\"></figure>";

        Assert.Equal(html, Cleaner().Clean(html));
    }

    /// <summary>A paragraph holding only an image has no text but is not empty.</summary>
    [Fact]
    public void AParagraphHoldingOnlyAnImageIsKept()
    {
        var html = "<p><img src=\"/a.png\"></p>" + ContactForm7;

        var result = Cleaner().Clean(html);

        Assert.Contains("<img", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputYieldsAnEmptyString(string? html) =>
        Assert.Equal(string.Empty, Cleaner().Clean(html));
}
