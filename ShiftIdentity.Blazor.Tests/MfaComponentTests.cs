using System.Net.Http.Json;
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
public sealed class MfaComponentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_setup_form_collects_the_required_proof_before_revealing_and_confirming_a_new_factor(bool replace)
    {
        var store = new RecordingStore(); var calls = 0;
        var purpose = replace ? AuthenticationOperationPurpose.MfaReplacement : AuthenticationOperationPurpose.MfaEnrollment;
        var transport = new ScriptedHttp(async request =>
        {
            var step = ++calls;
            if (step == 1) return Pending(replace ? AuthenticationStep.ExistingMfa : AuthenticationStep.Password, purpose);
            if (step == 2)
            {
                Assert.EndsWith(replace ? "/mfa/existing" : "/mfa/password", request.RequestUri!.AbsolutePath);
                if (replace) Assert.Equal("123456", (await request.Content!.ReadFromJsonAsync<CompleteMfaRequest>())!.Code);
                else Assert.Equal("current password", (await request.Content!.ReadFromJsonAsync<PasswordChangeProofRequest>())!.CurrentPassword);
                return Setup(purpose);
            }
            Assert.EndsWith("/mfa/confirm", request.RequestUri!.AbsolutePath);
            Assert.Equal("654321", (await request.Content!.ReadFromJsonAsync<CompleteMfaRequest>())!.Code);
            return new MfaChanged(AuthenticationFlowTests.Session());
        });
        using var context = Context(store, transport, out var flow);
        await flow.BeginMfaAsync("access", replace);
        var cut = context.Render<TotpEnrollmentForm>(p => p.Add(x => x.AdmissionFlow, flow));
        Assert.Empty(cut.FindAll("[data-testid=enrollment-secret]")); Assert.Empty(store.Writes);
        cut.Find("input").Input(replace ? "123456" : "current password"); await Submit(cut);
        Assert.Single(cut.FindAll("[data-testid=enrollment-secret]")); Assert.Empty(store.Writes);
        cut.Find("input").Input("654321"); await Submit(cut);
        Assert.Single(store.Writes); Assert.Null(flow.Pending); Assert.True(flow.MfaWasChanged);
    }

    [Fact]
    public async Task Mandatory_login_enrollment_uses_the_shared_setup_form_and_completes_once()
    {
        var store = new RecordingStore(); var completed = 0;
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(request.RequestUri!.AbsolutePath.EndsWith("/login")
            ? Setup(AuthenticationOperationPurpose.MfaEnrollment) : new MfaChanged(AuthenticationFlowTests.Session())));
        using var context = Context(store, transport, out var flow);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow).Add(x => x.SessionCompleted, _ => completed++));
        cut.FindAll("input")[0].Input("synthetic"); cut.FindAll("input")[1].Input("password"); await Submit(cut);
        Assert.Single(cut.FindComponents<TotpEnrollmentForm>()); Assert.Empty(store.Writes);
        cut.Find("input").Input("123456"); await Submit(cut);
        Assert.Equal(1, completed); Assert.Single(store.Writes);
    }

    [Fact]
    public async Task Actual_recovery_form_requires_both_credentials_then_returns_to_login_without_storing_a_session()
    {
        var store = new RecordingStore(); var rootAttempts = 0;
        var transport = new ScriptedHttp(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/recover"))
            {
                var body = await request.Content!.ReadFromJsonAsync<RecoverMfaRequest>();
                Assert.Equal("synthetic", body!.Username); Assert.Equal("current password", body.CurrentPassword);
                Assert.Equal("synthetic recovery code", body.RecoveryCode);
                return ++rootAttempts == 1 ? new AuthenticationRefused(AuthenticationFailure.InvalidProof) : Setup(AuthenticationOperationPurpose.MfaRecovery);
            }
            return new MfaChanged(new ReturnToLogin());
        });
        using var context = Context(store, transport, out var flow);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, flow));
        cut.FindAll("input")[0].Input("synthetic");
        cut.FindAll("button").Single(x => x.TextContent.Contains("Use an admin recovery code")).Click();
        Assert.Equal("synthetic", cut.FindAll("input")[0].GetAttribute("value"));
        for (var i = 0; i < 2; i++)
        {
            cut.FindAll("input")[0].Input("synthetic"); cut.FindAll("input")[1].Input("current password");
            cut.FindAll("input")[2].Input("synthetic recovery code"); await Submit(cut);
            if (i == 0) { Assert.Single(cut.FindAll("[data-testid=recovery-error]")); Assert.Empty(cut.FindAll("[data-testid=enrollment-secret]")); }
        }
        Assert.Single(cut.FindComponents<TotpEnrollmentForm>()); Assert.Empty(store.Writes);
        cut.Find("input").Input("123456"); await Submit(cut);
        Assert.Single(cut.FindAll("[data-testid=recovery-completed]")); Assert.True(flow.ReturnToLoginRequired);
        Assert.Empty(store.Writes); Assert.Null(flow.Pending);
    }

    [Fact]
    public async Task Incorrect_new_code_keeps_the_original_setup_and_cancellation_removes_it()
    {
        var store = new RecordingStore(); var calls = 0;
        var transport = new ScriptedHttp(request => Task.FromResult<AuthOutcome>(++calls == 1 ? Setup(AuthenticationOperationPurpose.MfaEnrollment)
            : request.RequestUri!.AbsolutePath.EndsWith("/cancel") ? new OperationCancelled() : new AuthenticationRefused(AuthenticationFailure.InvalidProof)));
        using var context = Context(store, transport, out var flow);
        await flow.BeginMfaAsync("access");
        var original = flow.Pending;
        var cut = context.Render<TotpEnrollmentForm>(p => p.Add(x => x.AdmissionFlow, flow));
        cut.Find("input").Input("000000"); await Submit(cut);
        Assert.Equal(original, flow.Pending); Assert.Single(cut.FindAll("[data-testid=mfa-error]"));
        cut.FindAll("button").Single(x => x.TextContent.Contains("Cancel setup")).Click();
        cut.WaitForAssertion(() => Assert.Null(flow.Pending));
        Assert.Empty(cut.FindAll("[data-testid=enrollment-secret]")); Assert.Empty(store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_ignores_late_MFA_completion_or_new_secret_and_revokes_a_rotated_handle(bool intermediate)
    {
        var store = new RecordingStore(); var pending = new TaskCompletionSource<AuthOutcome>(); var calls = 0;
        var transport = new ScriptedHttp(request => request.RequestUri!.AbsolutePath.EndsWith("/cancel") ? Task.FromResult<AuthOutcome>(new OperationCancelled())
            : ++calls == 1 ? Task.FromResult<AuthOutcome>(Setup(AuthenticationOperationPurpose.MfaEnrollment)) : pending.Task);
        var flow = new AuthenticationFlow(transport.Client(), store.Session);
        await flow.BeginMfaAsync("access");
        var completing = flow.ConfirmNewFactorAsync("123456");
        await flow.CancelAsync();
        pending.SetResult(intermediate ? Setup(AuthenticationOperationPurpose.MfaEnrollment) : new MfaChanged(AuthenticationFlowTests.Session()));
        Assert.IsType<AuthenticationRefused>(await completing);
        Assert.Empty(store.Writes); Assert.Null(flow.Pending); Assert.False(flow.MfaWasChanged);
        if (intermediate) Assert.Equal("setup-handle", transport.Requests.Last().Credential);
    }

    [Fact]
    public async Task Admin_form_requires_a_reference_and_does_not_display_a_late_code_for_a_different_target()
    {
        var store = new RecordingStore(); var response = new TaskCompletionSource<AuthOutcome>();
        var transport = new ScriptedHttp(_ => response.Task);
        using var context = Context(store, transport, out var flow);
        var cut = context.Render<MfaRecoveryAdminForm>(p => p.Add(x => x.AdmissionFlow, flow).Add(x => x.AccessToken, "admin-access").Add(x => x.UserID, 42));
        await cut.InvokeAsync(() => Assert.False(cut.FindComponent<EditForm>().Instance.EditContext!.Validate()));
        Assert.Empty(transport.Requests);
        cut.Find("input").Input("Synthetic independent check");
        var sending = Submit(cut);
        cut.Render(p => p.Add(x => x.AdmissionFlow, flow).Add(x => x.AccessToken, "admin-access").Add(x => x.UserID, 43));
        response.SetResult(new MfaRecoveryCodeIssued("synthetic code", DateTimeOffset.UtcNow.AddMinutes(15)));
        await sending;
        Assert.Empty(cut.FindAll("[data-testid=recovery-code]")); Assert.Empty(store.Writes);
    }

    private static ChallengeRequired Pending(AuthenticationStep step, AuthenticationOperationPurpose purpose) => new(new(step, "proof-handle", DateTimeOffset.UtcNow.AddMinutes(5), purpose));
    private static ChallengeRequired Setup(AuthenticationOperationPurpose purpose) => new(new(AuthenticationStep.NewMfa, "setup-handle", DateTimeOffset.UtcNow.AddMinutes(5), purpose,
        new("JBSWY3DPEHPK3PXP", "otpauth://totp/synthetic", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path d=\"M0 0h10v10z\"/></svg>")));
    private static BunitContext Context(RecordingStore store, ScriptedHttp transport, out AuthenticationFlow flow)
    {
        var context = new BunitContext(); var http = transport.Client(); flow = new(http, store.Session);
        context.Services.AddSingleton(http); context.Services.AddSingleton(store); context.Services.AddSingleton(store.Session);
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = config => config.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization(); context.JSInterop.Mode = JSRuntimeMode.Loose; return context;
    }
    private static Task Submit<T>(IRenderedComponent<T> cut) where T : class, Microsoft.AspNetCore.Components.IComponent =>
        cut.InvokeAsync(() => cut.FindComponent<EditForm>().Instance.OnValidSubmit.InvokeAsync(new EditContext(new object())));
}
