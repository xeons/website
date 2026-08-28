using XeonProductions.Infrastructure.Services;

namespace XeonProductions.Tests.Services;

public class HtmlServiceTests
{
    private static readonly HtmlService Html = new();

    // --- Layout classes the stylesheet implements, which must survive ---

    [Theory]
    [InlineData("alignleft")]
    [InlineData("alignright")]
    [InlineData("aligncenter")]
    [InlineData("alignnone")]
    [InlineData("wp-block-image")]
    [InlineData("wp-block-table")]
    [InlineData("is-style-rounded")]
    [InlineData("is-resized")]
    [InlineData("wp-caption")]
    [InlineData("wp-caption-text")]
    public void LayoutClassesAreKept(string name)
    {
        var result = Html.Sanitize($"<figure class=\"{name}\"><img src=\"/a.png\"></figure>");

        Assert.Contains(name, result);
    }

    // --- Classes that meant something only inside WordPress ---

    [Theory]
    [InlineData("wp-block-heading")]
    [InlineData("wp-block-list")]
    [InlineData("size-full")]
    [InlineData("size-medium")]
    [InlineData("is-style-default")]
    [InlineData("wp-image-490")]
    [InlineData("wp-image-1")]
    public void EditorArtifactsAreRemoved(string name)
    {
        var result = Html.Sanitize($"<p class=\"{name}\">Text</p>");

        Assert.DoesNotContain(name, result);
        Assert.Contains("Text", result);
    }

    [Fact]
    public void AMixedClassListKeepsOnlyWhatMeansSomething()
    {
        var result = Html.Sanitize(
            "<figure class=\"alignleft size-full is-resized wp-image-490\"><img src=\"/a.png\"></figure>");

        Assert.Contains("alignleft", result);
        Assert.Contains("is-resized", result);
        Assert.DoesNotContain("size-full", result);
        Assert.DoesNotContain("wp-image-490", result);
    }

    /// <summary>An attribute emptied of every name should go rather than sit there blank.</summary>
    [Fact]
    public void AnAttributeLeftEmptyIsDropped()
    {
        var result = Html.Sanitize("<h2 class=\"wp-block-heading\">About me</h2>");

        Assert.DoesNotContain("class", result);
        Assert.Contains("About me", result);
    }

    [Fact]
    public void AClassNameThatMerelyContainsAnArtifactIsKept()
    {
        var result = Html.Sanitize("<p class=\"my-size-full-layout\">Text</p>");

        Assert.Contains("my-size-full-layout", result);
    }

    /// <summary>
    /// This is a programming blog. A sample showing WordPress markup arrives entity encoded,
    /// so the rewrite must not reach inside it.
    /// </summary>
    [Fact]
    public void MarkupInsideACodeSampleIsUntouched()
    {
        var sample = "<pre><code>&lt;h2 class=&quot;wp-block-heading&quot;&gt;Hi&lt;/h2&gt;</code></pre>";

        var result = Html.Sanitize(sample);

        Assert.Contains("wp-block-heading", result);
        Assert.Contains("&lt;h2", result);
    }

    [Fact]
    public void ClassesOnUnrelatedContentAreLeftAlone()
    {
        var result = Html.Sanitize("<pre><code class=\"language-csharp\">var x = 1;</code></pre>");

        Assert.Contains("language-csharp", result);
    }

    // --- The sanitiser's own job, which the rewrite must not undermine ---

    [Fact]
    public void ScriptIsStillRemoved()
    {
        var result = Html.Sanitize("<p class=\"size-full\">Hi</p><script>alert(1)</script>");

        Assert.DoesNotContain("<script", result);
        Assert.DoesNotContain("alert", result);
    }

    [Fact]
    public void EventHandlersAreStillRemoved()
    {
        var result = Html.Sanitize("<p onclick=\"steal()\" class=\"wp-block-heading\">Hi</p>");

        Assert.DoesNotContain("onclick", result);
    }

    [Fact]
    public void ContentWithNoClassesIsReturnedUnchanged()
    {
        const string html = "<p>Nothing to do here.</p>";

        Assert.Equal(html, Html.Sanitize(html));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyInputYieldsAnEmptyString(string? html) =>
        Assert.Equal(string.Empty, Html.Sanitize(html));
}
