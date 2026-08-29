using Bunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using XeonProductions.Infrastructure.Data;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Components.Shared;

namespace XeonProductions.Tests.Components;

/// <summary>
/// The editor is created and destroyed through JavaScript, so its lifecycle is only
/// observable in the calls the component makes.
/// </summary>
public class RichTextEditorTests : BunitContext
{
    public RichTextEditorTests()
    {
        Services.AddLogging();
        JSInterop.Mode = JSRuntimeMode.Loose;

        // The image dropdown is a convenience the component gives up on quietly, so a
        // factory that cannot produce a context exercises the same path as an empty library.
        var factory = new Mock<IDbContextFactory<AppDbContext>>();
        factory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no database in a component test"));

        Services.AddSingleton(factory.Object);
        Services.AddSingleton(new Mock<IMediaService>().Object);
    }

    /// <summary>
    /// An editor left registered against markup that a navigation removed makes TinyMCE
    /// throw when the next one starts, and the editor never appears. Clearing up before
    /// creating one is what makes a second visit to an edit screen work.
    /// </summary>
    [Fact]
    public void ClearsAbandonedEditorsBeforeCreatingOne()
    {
        Render<RichTextEditor>();

        var call = JSInterop.Invocations["xeonEditor.releaseEditor"].Single();

        Assert.Single(call.Arguments);
        Assert.StartsWith("rte-", Assert.IsType<string>(call.Arguments[0]));
    }

    [Fact]
    public async Task ReleasesItsOwnEditorWhenItGoesAway()
    {
        var component = Render<RichTextEditor>();
        var created = (string)JSInterop.Invocations["xeonEditor.releaseEditor"].Single().Arguments[0]!;

        await component.Instance.DisposeAsync();

        var calls = JSInterop.Invocations["xeonEditor.releaseEditor"];

        Assert.Equal(2, calls.Count);
        Assert.All(calls, c => Assert.Equal(created, c.Arguments[0]));
    }

    /// <summary>Two editors on one screen must not release each other.</summary>
    [Fact]
    public void NamesEachEditorSeparately()
    {
        Render<RichTextEditor>();
        Render<RichTextEditor>();

        var ids = JSInterop.Invocations["xeonEditor.releaseEditor"]
            .Select(c => c.Arguments[0])
            .ToArray();

        Assert.Equal(2, ids.Length);
        Assert.Equal(2, ids.Distinct().Count());
    }
}
