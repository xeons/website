using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using XeonProductions.Domain.Entities;
using XeonProductions.Infrastructure.Services;
using XeonProductions.Web.Components.Pages.Admin;

namespace XeonProductions.Tests.Components;

/// <summary>
/// Guards the agreement between the field names the form renders and the prefix the form
/// binder reads. The two are declared in different places and nothing in the compiler ties
/// them together, so a mismatch compiles cleanly and fails only at runtime, for every
/// visitor, with a validation message that names the wrong cause.
/// </summary>
public class LoginTests : BunitContext
{
    public LoginTests()
    {
        Services.AddLogging();

        var userStore = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            userStore.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>().Object,
            null!, null!, null!, null!);

        var settings = new Mock<ISiteSettingsService>();
        settings.Setup(s => s.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SiteSettings { SiteTitle = "Xeon Productions" });

        Services.AddSingleton(userManager.Object);
        Services.AddSingleton(signInManager.Object);
        Services.AddSingleton(settings.Object);
    }

    /// <summary>
    /// The prefix the form binder looks under: the Name on the attribute, or the property
    /// name when Name is not set.
    /// </summary>
    private static string BinderPrefix(Type component)
    {
        var bound = component
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => new { Property = p, Attribute = p.GetCustomAttribute<SupplyParameterFromFormAttribute>() })
            .SingleOrDefault(x => x.Attribute is not null);

        Assert.NotNull(bound);
        return bound!.Attribute!.Name ?? bound.Property.Name;
    }

    /// <summary>The distinct prefixes appearing in the rendered input names.</summary>
    private static IReadOnlyList<string> RenderedPrefixes(IRenderedComponent<Login> page) =>
        page.FindAll("input[name]")
            .Select(e => e.GetAttribute("name") ?? string.Empty)
            .Where(n => n.Contains('.'))
            .Select(n => n[..n.IndexOf('.')])
            .Distinct()
            .ToList();

    [Fact]
    public void TheRenderedFieldNamesMatchThePrefixTheBinderReads()
    {
        var page = Render<Login>();

        var rendered = RenderedPrefixes(page);

        Assert.NotEmpty(rendered);
        Assert.Equal([BinderPrefix(typeof(Login))], rendered);
    }

    [Fact]
    public void TheFormOffersAnEmailAndAPasswordField()
    {
        var page = Render<Login>();
        var prefix = BinderPrefix(typeof(Login));

        var names = page.FindAll("input[name]")
            .Select(e => e.GetAttribute("name"))
            .ToList();

        Assert.Contains($"{prefix}.Email", names);
        Assert.Contains($"{prefix}.Password", names);
    }

    [Fact]
    public void ThePasswordFieldIsNotRenderedAsPlainText()
    {
        var page = Render<Login>();

        var password = page.FindAll("input")
            .Single(e => (e.GetAttribute("name") ?? string.Empty).EndsWith(".Password"));

        Assert.Equal("password", password.GetAttribute("type"));
    }

    [Fact]
    public void TheSiteTitleIsShown()
    {
        var page = Render<Login>();

        Assert.Contains("Xeon Productions", page.Find("h1").TextContent);
    }
}
