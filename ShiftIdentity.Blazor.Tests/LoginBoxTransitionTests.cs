using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The login box copies the outgoing view once per view change. A sign-in that ends in a challenge renders the box
/// twice for one change (the navigation to the new view, then the end of the submit handler). If the second render
/// copied the view again, it would copy the new view, and that copy stayed on top of the live form: the reported
/// duplicate button and doubled field labels on the recovery and code steps.
/// </summary>
[Trait("Category", "Ui")]
public sealed class LoginBoxTransitionTests
{
    [Fact]
    public void A_second_render_while_the_copy_is_being_taken_does_not_copy_the_new_view()
    {
        using var context = Context(out var prepare, out _);
        var cut = context.Render<LoginBox>(p => p.Add(x => x.ViewKey, "login").AddChildContent("<p>login</p>"));

        // The copy stays in progress while the parent renders again with the same new view.
        cut.Render(p => p.Add(x => x.ViewKey, "recovery").AddChildContent("<p>recovery</p>"));
        cut.Render(p => p.Add(x => x.ViewKey, "recovery").AddChildContent("<p>recovery</p>"));
        Assert.Single(prepare.Invocations);

        prepare.SetVoidResult();
        cut.Render(p => p.Add(x => x.ViewKey, "recovery").AddChildContent("<p>recovery</p>"));
        Assert.Single(prepare.Invocations);
    }

    [Fact]
    public void Each_change_of_view_takes_one_copy_and_a_render_without_a_change_takes_none()
    {
        using var context = Context(out var prepare, out var sync);
        prepare.SetVoidResult();
        var cut = context.Render<LoginBox>(p => p.Add(x => x.ViewKey, "login"));
        cut.Render(p => p.Add(x => x.ViewKey, "login"));
        Assert.Empty(prepare.Invocations);
        cut.Render(p => p.Add(x => x.ViewKey, "access"));
        cut.Render(p => p.Add(x => x.ViewKey, "recovery"));
        Assert.Equal(2, prepare.Invocations.Count);
        // Every render reports the current view, so the script can tell whether the view changed.
        Assert.Equal("recovery", sync.Invocations.Last().Arguments[1]);
    }

    [Fact]
    public void A_dialog_box_shows_its_caption_and_close_button_instead_of_the_logo_and_back()
    {
        using var context = Context(out var prepare, out _);
        prepare.SetVoidResult();
        var closed = 0;
        var cut = context.Render<LoginBox>(p => p.Add(x => x.Dialog, true).Add(x => x.Caption, "Change Password")
            .Add(x => x.Close, () => closed++).AddChildContent("<p>step</p>"));
        Assert.Equal("Change Password", cut.Find("[data-testid=security-dialog-caption]").TextContent);
        Assert.Empty(cut.FindAll(".identity-login-back"));
        Assert.Empty(cut.FindAll("img"));
        cut.Find("[data-testid=security-dialog-close]").Click();
        Assert.Equal(1, closed);
    }

    private static BunitContext Context(out JSRuntimeInvocationHandler prepare, out JSRuntimeInvocationHandler sync)
    {
        var context = new BunitContext();
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(o => o.LogoPath = "/logo.svg");
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var module = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/login-box.js");
        prepare = module.SetupVoid("prepare", _ => true);
        sync = module.SetupVoid("sync", _ => true);
        sync.SetVoidResult();
        return context;
    }
}
