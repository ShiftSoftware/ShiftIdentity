using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
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
public sealed class SecurityLinkComponentTests
{
    [Fact]
    public void Same_route_consumed_link_reopens_inertly_clears_previous_success_and_reports_refusal()
    {
        var opens = 0;
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out var transport, respond: request =>
            Task.FromResult<AuthOutcome>(request.RequestUri!.AbsolutePath.EndsWith("/open")
                ? ++opens == 1 ? Page(AuthenticationOperationPurpose.EmailVerify, "first") : new AuthenticationRefused(AuthenticationFailure.InvalidGrant)
                : new EmailVerificationCompleted()));
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-target]")));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Verify my email").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=email-verified]")));
        var same = cut.Instance;
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/VerifyEmail#grant=opaque&purpose=EmailVerify");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-error]")));
        Assert.Same(same, cut.Instance);
        Assert.Empty(cut.FindAll("[data-testid=email-verified]")); Assert.Empty(cut.FindAll("[data-testid=security-link-target]"));
        Assert.Equal(3, transport.Requests.Count);
        Assert.Equal(2, context.JSInterop.Invocations.Count(x => x.Identifier == "clearFragment"));
        Assert.Empty(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    [Fact]
    public void Same_route_new_reset_link_discards_password_fields_and_old_page_handle()
    {
        var opens = 0;
        using var context = Setup(AuthenticationOperationPurpose.PasswordResetEmail, out var ui, out var transport,
            respond: _ => Task.FromResult<AuthOutcome>(Page(AuthenticationOperationPurpose.PasswordResetEmail, ++opens == 1 ? "first" : "second")));
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("form")));
        cut.FindAll("input")[0].Input("Old password entry"); cut.FindAll("input")[1].Input("Old password entry");
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/ResetPassword#grant=second&purpose=PasswordResetEmail");
        cut.WaitForAssertion(() => Assert.Contains("second", cut.Find("[data-testid=security-link-target]").TextContent));
        Assert.All(cut.FindAll("input"), input => Assert.True(string.IsNullOrEmpty(input.GetAttribute("value"))));
        Assert.Equal(2, transport.Requests.Count); Assert.Contains("\"grant\":\"second\"", transport.Requests.Last().Body);
        Assert.DoesNotContain("page-first", cut.Markup);
    }

    [Fact]
    public async Task Navigation_during_open_discards_the_late_page_and_processes_latest_fragment_once()
    {
        var first = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously); var opens = 0;
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out var transport,
            respond: _ => ++opens == 1 ? first.Task : Task.FromResult<AuthOutcome>(Page(AuthenticationOperationPurpose.EmailVerify, "second")));
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, true));
        cut.WaitForAssertion(() => Assert.Single(transport.Requests));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/VerifyEmail#grant=second&purpose=EmailVerify");
        await cut.InvokeAsync(() => first.SetResult(Page(AuthenticationOperationPurpose.EmailVerify, "first")));
        cut.WaitForAssertion(() => Assert.Contains("second", cut.Find("[data-testid=security-link-target]").TextContent));
        Assert.Equal(2, transport.Requests.Count); Assert.Empty(cut.FindAll("[data-testid=security-link-error]"));
        Assert.Empty(cut.FindAll("[data-testid=email-verified]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Navigation_or_cancel_during_completion_discards_late_success_without_session_changes(bool cancel)
    {
        var complete = new TaskCompletionSource<AuthOutcome>(TaskCreationOptions.RunContinuationsAsynchronously); var opens = 0;
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out var transport,
            respond: request => request.RequestUri!.AbsolutePath.EndsWith("/complete") ? complete.Task
                : Task.FromResult<AuthOutcome>(Page(AuthenticationOperationPurpose.EmailVerify, ++opens == 1 ? "first" : "second")));
        var previous = AuthenticationFlowTests.Session().Session; await ui.Store.StoreTokenAsync(previous);
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-target]")));
        var submit = cut.FindAll("button").Single(b => b.TextContent.Trim() == "Verify my email").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());
        cut.WaitForAssertion(() => Assert.Equal(2, transport.Requests.Count));
        if (cancel) cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        else context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/VerifyEmail#grant=second&purpose=EmailVerify");
        await cut.InvokeAsync(() => complete.SetResult(new EmailVerificationCompleted()));
        await submit;
        if (cancel)
        {
            Assert.EndsWith(SecurityLinkNavigation.LoginAfterCancel, context.Services.GetRequiredService<NavigationManager>().Uri);
            Assert.Equal(2, transport.Requests.Count); Assert.Empty(cut.FindAll("[data-testid=security-link-target]"));
        }
        else
        {
            cut.WaitForAssertion(() => Assert.Contains("second", cut.Find("[data-testid=security-link-target]").TextContent));
            Assert.Equal(3, transport.Requests.Count);
        }
        Assert.Empty(cut.FindAll("[data-testid=email-verified]"));
        Assert.Same(previous, Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes));
    }

    private static SecurityLinkOpened Page(AuthenticationOperationPurpose purpose, string target) =>
        new("page-" + target, target + "***@example.invalid", purpose, DateTimeOffset.UtcNow.AddMinutes(10));

    [Theory]
    [InlineData(AuthenticationOperationPurpose.PasswordResetEmail)]
    [InlineData(AuthenticationOperationPurpose.PasswordResetManual)]
    [InlineData(AuthenticationOperationPurpose.EmailVerify)]
    public async Task Landing_is_inert_clears_url_and_targets_grant_despite_other_browser_session(AuthenticationOperationPurpose purpose)
    {
        using var context = Setup(purpose, out var ui, out var transport);
        await ui.Store.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, purpose == AuthenticationOperationPurpose.EmailVerify));
        cut.WaitForAssertion(() => Assert.Contains("t***@example.invalid", cut.Find("[data-testid=security-link-target]").TextContent));
        var call = Assert.Single(transport.Requests);
        Assert.Contains("\"grant\":\"opaque\"", call.Body);
        Assert.DoesNotContain("synthetic-refresh", cut.Markup);
        Assert.Null(call.Scheme);
        Assert.Single(context.JSInterop.Invocations, x => x.Identifier == "clearFragment");
        Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    [Fact]
    public async Task Email_requires_explicit_verify_submit_and_creates_no_session()
    {
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out var transport);
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-target]")));
        Assert.Single(transport.Requests);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Verify my email").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=email-verified]")));
        Assert.Equal(2, transport.Requests.Count);
        Assert.Contains("\"pageHandle\":\"page-handle\"", transport.Requests[1].Body);
        Assert.Empty(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    [Fact]
    public async Task Reset_requires_matching_typed_password_and_returns_to_login_without_replacing_other_session()
    {
        using var context = Setup(AuthenticationOperationPurpose.PasswordResetEmail, out var ui, out var transport);
        var previous = AuthenticationFlowTests.Session().Session; await ui.Store.StoreTokenAsync(previous);
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("form")));
        cut.FindAll("input")[0].Input("Synthetic new password!"); cut.FindAll("input")[1].Input("mismatch");
        cut.Find("form").Submit(); Assert.Single(transport.Requests);
        Assert.Contains("Passwords must match.", cut.Markup);
        cut.FindAll("input")[1].Input("Synthetic new password!"); cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.EndsWith(SecurityLinkNavigation.LoginAfterReset, context.Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Equal(2, transport.Requests.Count); Assert.Contains("Synthetic new password!", transport.Requests[1].Body);
        Assert.Same(previous, Assert.Single(context.Services.GetRequiredService<RecordingStore>().Writes));
    }

    [Fact]
    public void Cancel_discards_page_context_without_completing_the_grant()
    {
        using var context = Setup(AuthenticationOperationPurpose.PasswordResetEmail, out var ui, out var transport);
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-target]")));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Cancel").Click();
        Assert.Single(transport.Requests);
        Assert.EndsWith(SecurityLinkNavigation.LoginAfterCancel, context.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    public void Stale_completion_shows_local_error_and_requires_new_link()
    {
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out var transport, refuse: true);
        var cut = context.Render<SecurityLinkForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, true));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-target]")));
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Verify my email").Click();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-error]")));
        Assert.Empty(cut.FindAll("[data-testid=security-link-target]")); Assert.Equal(2, transport.Requests.Count);
        Assert.Empty(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Public_requests_submit_saved_identifier_and_show_generic_result(bool verification)
    {
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out var transport);
        var cut = context.Render<SecurityEmailRequestForm>(p => p.Add(x => x.Context, ui).Add(x => x.Verification, verification));
        cut.Find("input").Input("unknown@example.invalid"); cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Contains("If an eligible account matches", cut.Markup));
        Assert.Contains("unknown@example.invalid", Assert.Single(transport.Requests).Body);
        Assert.Empty(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    [Fact]
    public void Reset_login_marker_keeps_the_login_form_visible_for_an_existing_browser_user()
    {
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out _, authorized: true);
        var nav = context.Services.GetRequiredService<NavigationManager>(); nav.NavigateTo(SecurityLinkNavigation.LoginAfterReset);
        var cut = context.Render<CascadingAuthenticationState>(p => p.AddChildContent<CascadingValue<AdmissionUiContext>>(v =>
            v.Add(x => x.Value, ui).AddChildContent<LoginForm>()));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("form")));
        Assert.EndsWith(SecurityLinkNavigation.LoginAfterReset, nav.Uri);
        Assert.Contains("Password reset. Sign in with your new password.", cut.Markup);
    }

    [Theory]
    [InlineData(AuthenticationOperationPurpose.Login)]
    [InlineData(AuthenticationOperationPurpose.PasswordChange)]
    public void Email_gate_keeps_verification_request_reachable_after_other_required_steps(AuthenticationOperationPurpose pendingPurpose)
    {
        using var context = Setup(AuthenticationOperationPurpose.EmailVerify, out var ui, out _, loginGate: pendingPurpose);
        var cut = context.Render<LoginForm>(p => p.Add(x => x.AdmissionFlow, ui.Flow));
        cut.FindAll("input")[0].Input("synthetic"); cut.FindAll("input")[1].Input("password");
        cut.Find("form").Submit();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("a[href='Identity/SendEmailVerificationLink']")));
        Assert.Contains("Verify your saved email address before signing in.", cut.Markup);
        Assert.Empty(context.Services.GetRequiredService<RecordingStore>().Writes);
    }

    private static BunitContext Setup(AuthenticationOperationPurpose purpose, out AdmissionUiContext ui, out ScriptedHttp transport,
        bool refuse = false, bool authorized = false, AuthenticationOperationPurpose? loginGate = null,
        Func<HttpRequestMessage, Task<AuthOutcome>>? respond = null)
    {
        var context = new BunitContext(); var store = new RecordingStore();
        transport = new ScriptedHttp(respond ?? (request => Task.FromResult<AuthOutcome>(request.RequestUri!.AbsolutePath.EndsWith("/login") && loginGate is { } gate
            ? new ChallengeRequired(new(AuthenticationStep.EmailVerification, null, DateTimeOffset.UtcNow.AddMinutes(10), gate))
            : request.RequestUri!.AbsolutePath.EndsWith("/open")
            ? new SecurityLinkOpened("page-handle", "t***@example.invalid", purpose, DateTimeOffset.UtcNow.AddMinutes(10))
            : request.RequestUri.AbsolutePath.EndsWith("/request") ? new SecurityDeliveryRequested()
            : refuse ? new AuthenticationRefused(AuthenticationFailure.StaleOperation)
            : purpose == AuthenticationOperationPurpose.EmailVerify ? new EmailVerificationCompleted() : new ReturnToLogin())));
        var http = transport.Client(); ui = new(new AuthenticationFlow(http, store.Session), store.Session, http);
        context.Services.AddSingleton(http); context.Services.AddSingleton(store); context.Services.AddSingleton(store.Session);
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization();
        if (authorized) auth.SetAuthorized("different-browser-user");
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/security-link.js")
            .Setup<bool>("clearFragment", _ => true).SetResult(true);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/" +
            (purpose == AuthenticationOperationPurpose.EmailVerify ? "VerifyEmail" : "ResetPassword") + "#grant=opaque&purpose=" + purpose);
        return context;
    }
}
