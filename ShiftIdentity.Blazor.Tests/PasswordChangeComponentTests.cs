using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class PasswordChangeComponentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_required_form_sends_new_password_and_finishes_applicable_MFA(bool mfa)
    {
        var store = new RecordingStore(); var completed = 0;
        var transport = new ScriptedHttp(async request =>
        {
            var route = request.RequestUri!.AbsolutePath;
            if (route.EndsWith("/login")) return Challenge(AuthenticationStep.PasswordChange);
            if (route.EndsWith("/complete"))
            {
                var body = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<CompletePasswordChangeRequest>(request.Content!);
                Assert.Equal("A long new synthetic password", body!.NewPassword);
                return mfa ? Challenge(AuthenticationStep.ExistingMfa) : new PasswordChanged(AuthenticationFlowTests.Session());
            }
            Assert.EndsWith("password-change/mfa", route);
            var proof = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<CompleteMfaRequest>(request.Content!);
            Assert.Equal("123456", proof!.Code);
            return new PasswordChanged(AuthenticationFlowTests.Session());
        });
        using var context = Context(store, transport, out var flow);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow).Add(x => x.SessionCompleted, _ => completed++));
        cut.FindAll("input")[0].Input("synthetic"); cut.FindAll("input")[1].Input("password");
        await Submit(cut);
        Assert.Single(cut.FindComponents<ChangePasswordForm>()); Assert.Empty(store.Writes);
        cut.FindAll("input")[0].Input("A long new synthetic password"); cut.FindAll("input")[1].Input("A long new synthetic password");
        await Submit(cut);
        if (mfa)
        {
            Assert.Empty(store.Writes); Assert.Single(cut.FindComponents<MfaForm>());
            cut.Find("input").Input("123456"); await Submit(cut);
        }
        Assert.Single(store.Writes); Assert.Equal(1, completed);
    }

    [Fact]
    public async Task Current_password_and_confirmation_are_validated_by_the_existing_change_form()
    {
        var store = new RecordingStore(); var calls = 0;
        var transport = new ScriptedHttp(async request =>
        {
            if (++calls == 1) return Challenge(AuthenticationStep.Password);
            var proof = await System.Net.Http.Json.HttpContentJsonExtensions.ReadFromJsonAsync<PasswordChangeProofRequest>(request.Content!);
            Assert.Equal("the current password", proof!.CurrentPassword);
            return Challenge(AuthenticationStep.PasswordChange);
        });
        using var context = Context(store, transport, out var flow);
        await flow.BeginPasswordChangeAsync("access");
        var cut = context.Render<ChangePasswordForm>(p => p.Add(x => x.AdmissionFlow, flow));
        cut.Find("input").Input("the current password"); await Submit(cut);
        Assert.Contains(NewPasswordPolicy.Guidance, cut.Markup);
        cut.FindAll("input")[0].Input("A long new synthetic password"); cut.FindAll("input")[1].Input("does not match");
        await cut.InvokeAsync(() => Assert.False(cut.FindComponent<EditForm>().Instance.EditContext!.Validate()));
        Assert.Equal(2, calls); Assert.Empty(store.Writes);
    }

    private static ChallengeRequired Challenge(AuthenticationStep step) =>
        new(new(step, "opaque", DateTimeOffset.UtcNow.AddMinutes(5), AuthenticationOperationPurpose.PasswordChange));
    private static BunitContext Context(RecordingStore store, ScriptedHttp transport, out AuthenticationFlow flow)
    {
        var context = new BunitContext(); var http = transport.Client(); flow = new(http, store);
        context.Services.AddSingleton(http); context.Services.AddSingleton<IIdentityStore>(store);
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization(); context.JSInterop.Mode = JSRuntimeMode.Loose; return context;
    }
    private static Task Submit<T>(IRenderedComponent<T> cut) where T : class, Microsoft.AspNetCore.Components.IComponent =>
        cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync(new EditContext(new object())));
}
