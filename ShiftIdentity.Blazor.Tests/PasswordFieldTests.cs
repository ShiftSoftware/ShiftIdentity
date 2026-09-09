using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class PasswordFieldTests
{
    [Theory]
    [InlineData("Password", "current-password")]
    [InlineData("Current Password", "current-password")]
    [InlineData("New Password", "new-password")]
    [InlineData("Confirm Password", "new-password")]
    public void Visibility_toggle_preserves_value_binding_and_validation_state(string label, string autocomplete)
    {
        using var context = Context(); var changes = 0;
        var cut = context.Render<PasswordField>(p => p.Add(x => x.Label, label).Add(x => x.Value, "synthetic password")
            .Add(x => x.ValueChanged, _ => changes++).Add(x => x.Error, true).Add(x => x.ErrorText, "Existing validation message")
            .AddUnmatched("autocomplete", autocomplete));
        Assert.Equal("password", cut.Find("input").GetAttribute("type"));
        Assert.Equal(autocomplete, cut.Find("input").GetAttribute("autocomplete"));
        Assert.Equal("button", cut.Find("button").GetAttribute("type"));
        cut.Find($"button[aria-label='Show {label}']").Click();
        Assert.Equal("text", cut.Find("input").GetAttribute("type"));
        Assert.Equal("synthetic password", cut.Find("input").GetAttribute("value"));
        Assert.Contains("Existing validation message", cut.Markup); Assert.Equal(0, changes);
        cut.Find($"button[aria-label='Hide {label}']").Click();
        Assert.Equal("password", cut.Find("input").GetAttribute("type")); Assert.Equal(0, changes);
    }

    [Fact]
    public void Showing_an_empty_saved_secret_preserves_placeholder_and_does_not_generate_or_load_a_value()
    {
        using var context = Context(); var changes = 0;
        var cut = context.Render<PasswordField>(p => p.Add(x => x.Label, "API secret").Add(x => x.Placeholder, "******")
            .Add(x => x.ValueChanged, _ => changes++));
        cut.Find("button").Click();
        Assert.Equal("******", cut.Find("input").GetAttribute("placeholder"));
        Assert.True(string.IsNullOrEmpty(cut.Find("input").GetAttribute("value"))); Assert.Equal(0, changes);
    }

    [Fact]
    public void Toggling_a_typed_password_keeps_the_entered_text_and_notifies_the_binding_only_for_the_edit()
    {
        using var context = Context(); var changes = new List<string>();
        var cut = context.Render<PasswordField>(p => p.Add(x => x.Label, "Current Password")
            .Add(x => x.Immediate, true).Add(x => x.ValueChanged, value => changes.Add(value)));
        cut.Find("input").Input("Typed synthetic password");
        cut.Find("button").Click();
        Assert.Equal("Typed synthetic password", cut.Find("input").GetAttribute("value"));
        cut.Find("button").Click();
        Assert.Equal("Typed synthetic password", cut.Find("input").GetAttribute("value"));
        Assert.Equal(["Typed synthetic password"], changes);
    }

    [Fact]
    public void Disabled_and_readonly_field_contracts_are_preserved()
    {
        using var context = Context();
        var cut = context.Render<PasswordField>(p => p.Add(x => x.Label, "Secret").Add(x => x.ReadOnly, true));
        Assert.True(cut.Find("input").HasAttribute("readonly"));
        cut.Render(p => p.Add(x => x.Disabled, true));
        Assert.True(cut.Find("input").HasAttribute("disabled")); Assert.Empty(cut.FindAll("button"));
    }

    private static BunitContext Context()
    {
        var context = new BunitContext();
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}
