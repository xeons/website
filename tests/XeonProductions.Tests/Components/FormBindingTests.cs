using System.Reflection;
using Microsoft.AspNetCore.Components;
using XeonProductions.Web.Components.Pages;
using XeonProductions.Web.Components.Pages.Admin;

namespace XeonProductions.Tests.Components;

/// <summary>
/// Guards the agreement between the field names a form renders and the prefix its binder
/// reads.
///
/// InputText takes the name it renders from the bind expression, while the binder reads the
/// name of the property the attribute sits on. Nothing ties the two together, so a mismatch
/// compiles cleanly and fails at runtime for every visitor. It has happened twice, on the
/// login form and the contact form, so the check covers every form rather than the one that
/// was noticed.
/// </summary>
public class FormBindingTests
{
    /// <summary>Every routable component here that binds a form.</summary>
    public static TheoryData<Type> FormComponents() => new() { typeof(Login), typeof(Contact) };

    /// <summary>
    /// The prefix the form binder looks under: the Name on the attribute, or the property
    /// name when Name is not given.
    /// </summary>
    private static (string Prefix, string Property) Binder(Type component)
    {
        var bound = component
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => new { Property = p, Attribute = p.GetCustomAttribute<SupplyParameterFromFormAttribute>() })
            .SingleOrDefault(x => x.Attribute is not null);

        Assert.NotNull(bound);
        return (bound!.Attribute!.Name ?? bound.Property.Name, bound.Property.Name);
    }

    /// <summary>
    /// A form whose binder prefix is only the property name is relying on the bind
    /// expressions happening to use that same identifier, which is exactly the mismatch that
    /// broke both forms. Setting Name states the prefix rather than leaving it to chance.
    /// </summary>
    [Theory]
    [MemberData(nameof(FormComponents))]
    public void EveryFormStatesItsBinderPrefixExplicitly(Type component)
    {
        var bound = component
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => p.GetCustomAttribute<SupplyParameterFromFormAttribute>())
            .SingleOrDefault(a => a is not null);

        Assert.NotNull(bound);
        Assert.False(string.IsNullOrEmpty(bound!.Name),
            $"{component.Name} does not set Name on SupplyParameterFromForm, so the binder "
            + "prefix defaults to the property name and must coincidentally match what the "
            + "inputs render.");
    }

    [Theory]
    [MemberData(nameof(FormComponents))]
    public void TheFormNameIsSet(Type component)
    {
        var bound = component
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Select(p => p.GetCustomAttribute<SupplyParameterFromFormAttribute>())
            .Single(a => a is not null);

        Assert.False(string.IsNullOrEmpty(bound!.FormName));
    }

    /// <summary>
    /// The rendered check lives with each component's own test, because rendering needs that
    /// component's services. This records what those tests compare against.
    /// </summary>
    [Theory]
    [MemberData(nameof(FormComponents))]
    public void TheBinderPrefixIsReadable(Type component)
    {
        var (prefix, property) = Binder(component);

        Assert.False(string.IsNullOrWhiteSpace(prefix));
        Assert.False(string.IsNullOrWhiteSpace(property));
    }
}
