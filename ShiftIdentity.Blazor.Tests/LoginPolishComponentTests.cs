using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

[Trait("Category", "Ui")]
public sealed class LoginPolishComponentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Login_clients_trim_username_without_changing_case_internal_spaces_or_password(bool staged)
    {
        using var transport = new Transport(staged) { Success = true };
        await using var context = Context(transport, out _);
        var request = new LoginDTO { Username = " \tSynthetic  User\r\n", Password = "  Synthetic password 39!\t" };
        if (staged) await context.Services.GetRequiredService<AuthenticationFlow>().LoginAsync(request.Username, request.Password);
        else await context.Services.GetRequiredService<ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services.AuthService>().LoginAsync(request);
        using var body = JsonDocument.Parse(Assert.Single(transport.Bodies));
        Assert.Equal("Synthetic  User", body.RootElement.GetProperty("username").GetString());
        Assert.Equal(request.Password, body.RootElement.GetProperty("password").GetString());
        Assert.Equal(" \tSynthetic  User\r\n", request.Username);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inline_failure_offers_help_and_opens_an_inert_reset_request(bool staged)
    {
        using var transport = new Transport(staged);
        await using var context = Context(transport, out var store);
        var cut = context.Render<LoginForm>();
        Assert.Empty(cut.FindAll("[data-testid=login-recovery-choice]"));
        Assert.DoesNotContain("Verify email", cut.Markup);
        await Login(cut);
        var panel = cut.Find("[data-login-failure]");
        Assert.Equal("true", panel.ParentElement!.ParentElement!.ParentElement!.GetAttribute("data-open"));
        Assert.Contains("Username or password is incorrect", panel.TextContent);
        Assert.DoesNotContain("server-private-detail", panel.TextContent);
        Assert.DoesNotContain("Synthetic password", panel.TextContent);
        Assert.DoesNotContain("Synthetic  User", panel.TextContent);
        Assert.Empty(cut.FindAll("[role=dialog]"));
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
        cut.Find("[data-testid=login-failure-help]").Click();
        cut.WaitForElement("[data-testid=login-reset-choice]").Click();
        cut.WaitForElement("[data-testid=security-email-request], form");
        Assert.Contains("view=reset", context.Services.GetRequiredService<NavigationManager>().Uri);
        Assert.Single(transport.Bodies); Assert.Empty(store.Writes);
        Assert.Single(cut.FindAll(".identity-login-box"));
        if (staged)
        {
            Assert.Contains("Username or email", cut.Markup);
            cut.Find("input").Input("synthetic@example.invalid");
            cut.Find("form").Submit();
            cut.WaitForElement("[data-testid=delivery-requested]");
            Assert.Equal(2, transport.Bodies.Count);
            Assert.Contains("Check your inbox", cut.Markup);
            Assert.DoesNotContain("eligible account", cut.Markup);
            Assert.Contains("synthetic@example.invalid", transport.Bodies.Last());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_clears_failure_trims_username_and_keeps_password_bytes(bool staged)
    {
        using var transport = new Transport(staged);
        await using var context = Context(transport, out var store);
        var cut = context.Render<LoginForm>();
        await Login(cut);
        Assert.Equal(string.Empty, cut.FindAll("input")[1].GetAttribute("value") ?? "");
        transport.Success = true;
        await Login(cut);
        Assert.Single(store.Writes);
        Assert.Empty(cut.FindAll(".auth-feedback-slot[data-open=true] [data-login-failure]"));
        Assert.Empty(cut.FindAll("[data-testid=admission-error]"));
        Assert.All(transport.Bodies, body =>
        {
            using var json = JsonDocument.Parse(body);
            Assert.Equal("Synthetic  User", json.RootElement.GetProperty("username").GetString());
            Assert.Equal("  Synthetic password 39!\t", json.RootElement.GetProperty("password").GetString());
        });
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Service_or_connection_failure_is_distinct_and_never_renders_response_details(bool staged, bool disconnected)
    {
        using var transport = new Transport(staged) { Unavailable = true, Disconnected = disconnected };
        await using var context = Context(transport, out _);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<LoginForm>();
        await Login(cut);
        Assert.Contains("LOGIN-SERVICE", cut.Markup);
        Assert.DoesNotContain("Username or password is incorrect", cut.Markup);
        Assert.DoesNotContain("server-private-detail", cut.Markup);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Retry_waits_for_the_previous_panel_to_close_before_sending_and_blocks_duplicates(bool staged)
    {
        using var transport = new Transport(staged);
        await using var context = Context(transport, out _);
        var module = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/login-box.js");
        var exit = module.SetupVoid("waitForFeedbackExit", _ => true);
        var cut = context.Render<LoginForm>();
        await Login(cut);
        Assert.Empty(exit.Invocations);

        var retry = Login(cut);
        cut.WaitForAssertion(() => Assert.Single(exit.Invocations));
        Assert.Single(transport.Bodies);
        Assert.Equal("true", cut.Find("form").GetAttribute("aria-busy"));
        Assert.Empty(cut.FindAll(".auth-feedback-slot[data-open=true] [data-login-failure]"));
        Assert.Contains("Signing in", cut.Find("button[type=submit]").TextContent);
        Assert.All(cut.FindAll("input"), input => Assert.True(input.HasAttribute("disabled")));
        await cut.Find("form").SubmitAsync();
        Assert.Single(transport.Bodies);

        exit.SetVoidResult();
        await retry;
        Assert.Equal(2, transport.Bodies.Count);
        Assert.Single(cut.FindAll(".auth-feedback-slot[data-open=true] [data-login-failure]"));
        Assert.Equal("false", cut.Find("form").GetAttribute("aria-busy"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Leaving_during_the_panel_exit_does_not_send_the_queued_login(bool dispose)
    {
        using var transport = new Transport(true);
        await using var context = Context(transport, out _);
        var module = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/login-box.js");
        var exit = module.SetupVoid("waitForFeedbackExit", _ => true);
        var cut = context.Render<LoginForm>();
        await Login(cut);
        var retry = Login(cut);
        cut.WaitForAssertion(() => Assert.Single(exit.Invocations));
        if (dispose) cut.Instance.Dispose();
        else
        {
            context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?view=access");
            cut.WaitForElement("[data-testid=login-reset-choice]");
        }
        exit.SetVoidResult();
        await retry;
        Assert.Single(transport.Bodies);
    }

    [Theory]
    [InlineData(AuthenticationStep.PasswordChange)]
    [InlineData(AuthenticationStep.ExistingMfa)]
    [InlineData(AuthenticationStep.NewMfa)]
    [InlineData(AuthenticationStep.MfaRecovery)]
    [InlineData(AuthenticationStep.EmailVerification)]
    public async Task Normal_staged_host_preserves_every_valid_challenge(AuthenticationStep step)
    {
        using var transport = new Transport(true) { Step = step };
        await using var context = Context(transport, out var store);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<LoginForm>();
        await Login(cut);
        Assert.Equal(step, context.Services.GetRequiredService<AuthenticationFlow>().Pending?.Step);
        Assert.Empty(store.Writes); Assert.Empty(dialogs.FindAll("[role=dialog]"));
        Assert.Empty(cut.FindAll("[data-testid=admission-error]"));
        if (step == AuthenticationStep.MfaRecovery) Assert.Single(cut.FindComponents<MfaRecoveryForm>());
        else if (step == AuthenticationStep.PasswordChange) Assert.Single(cut.FindComponents<ChangePasswordForm>());
        else if (step == AuthenticationStep.ExistingMfa) Assert.Single(cut.FindComponents<MfaForm>());
        else if (step == AuthenticationStep.NewMfa) Assert.Single(cut.FindComponents<TotpEnrollmentForm>());
        else Assert.Single(cut.FindAll("[data-testid=restricted-step]"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_submit_or_disposed_form_cannot_show_a_late_failure(bool staged)
    {
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport(staged) { Wait = response.Task };
        await using var context = Context(transport, out var store);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<LoginForm>();
        var submit = Login(cut);
        cut.WaitForAssertion(() => Assert.Single(transport.Bodies));
        var duplicate = cut.Find("form").SubmitAsync();
        Assert.Single(transport.Bodies);
        cut.Instance.Dispose();
        response.SetResult();
        await Task.WhenAll(submit, duplicate);
        Assert.Empty(cut.FindAll(".auth-feedback-slot[data-open=true] [data-login-failure]")); Assert.Empty(store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Whitespace_only_username_reaches_existing_refusal_as_empty(bool staged)
    {
        using var transport = new Transport(staged);
        await using var context = Context(transport, out var store);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<LoginForm>();
        await Login(cut, " \t ");
        using var body = JsonDocument.Parse(Assert.Single(transport.Bodies));
        Assert.Equal("", body.RootElement.GetProperty("username").GetString());
        Assert.Empty(store.Writes);
    }

    [Fact]
    public async Task Browser_history_changes_restore_views_without_sending_requests()
    {
        using var transport = new Transport(true);
        await using var context = Context(transport, out _);
        var nav = context.Services.GetRequiredService<NavigationManager>();
        var cut = context.Render<LoginForm>();
        cut.Find("[data-testid=login-help]").Click();
        cut.WaitForElement("[data-testid=login-recovery-choice]").Click();
        cut.WaitForElement("[data-testid=mfa-recovery-form]");
        Assert.Empty(cut.FindAll(".identity-login-box .mud-paper"));
        nav.NavigateTo("/?view=access");
        cut.WaitForElement("[data-testid=login-reset-choice]");
        nav.NavigateTo("/");
        cut.WaitForElement("[data-testid=login-help]");
        Assert.Empty(transport.Bodies);
        Assert.Single(cut.FindAll("form"));
    }

    [Fact]
    public async Task Top_back_button_uses_browser_history_for_an_internal_view()
    {
        using var transport = new Transport(true);
        await using var context = Context(transport, out _);
        var cut = context.Render<LoginForm>();
        cut.Find("[data-testid=login-help]").Click();
        cut.WaitForElement("[data-testid=login-reset-choice]").Click();
        cut.WaitForElement("[data-testid=security-email-request]");
        cut.Find(".identity-login-back").Click();
        Assert.Contains(context.JSInterop.Invocations, call => call.Identifier == "history.back");
        Assert.Empty(transport.Bodies);
    }

    [Fact]
    public async Task Top_back_from_a_direct_reset_link_returns_through_help_without_leaving_login()
    {
        using var transport = new Transport(true);
        await using var context = Context(transport, out _);
        var nav = context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/?view=reset");
        var cut = context.Render<LoginForm>();
        cut.WaitForElement("[data-testid=security-email-request]");
        cut.Find(".identity-login-back").Click();
        cut.WaitForElement("[data-testid=login-reset-choice]");
        cut.Find(".identity-login-back").Click();
        cut.WaitForElement("[data-testid=login-help]");
        Assert.DoesNotContain(context.JSInterop.Invocations, call => call.Identifier == "history.back");
        Assert.Empty(transport.Bodies);
    }

    [Fact]
    public async Task Navigating_back_from_a_pending_login_discards_late_failure()
    {
        var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var transport = new Transport(true) { Wait = response.Task };
        await using var context = Context(transport, out var store);
        var cut = context.Render<LoginForm>();
        var submit = Login(cut);
        cut.WaitForAssertion(() => Assert.Single(transport.Bodies));
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/?view=access");
        response.SetResult();
        await submit;
        cut.WaitForElement("[data-testid=login-reset-choice]");
        Assert.Empty(cut.FindAll("[data-login-failure]"));
        Assert.Empty(store.Writes);
    }

    // A host whose scoped styles did not load shows everything in the markup, so the failure text must not be there
    // before a sign-in attempt, and must be gone again once a retry has succeeded.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_text_is_rendered_only_after_a_failed_attempt(bool staged)
    {
        using var transport = new Transport(staged);
        await using var context = Context(transport, out _);
        var cut = context.Render<LoginForm>();
        Assert.Empty(cut.FindAll("[data-testid=login-failure-message]"));
        Assert.DoesNotContain("Username or password is incorrect", cut.Markup);
        await Login(cut);
        Assert.Contains("Username or password is incorrect", cut.Find("[data-testid=login-failure-message]").TextContent);
        transport.Success = true;
        await Login(cut);
        Assert.Empty(cut.FindAll("[data-testid=login-failure-message]"));
    }

    // The code step's error used to stay in place on a second wrong code. It now slides closed before the code goes out.
    [Fact]
    public async Task A_second_wrong_code_at_sign_in_closes_the_error_before_the_code_is_sent()
    {
        using var transport = new Transport(true) { Step = AuthenticationStep.ExistingMfa };
        await using var context = Context(transport, out _);
        var exit = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js")
            .SetupVoid("waitForClose", _ => true);
        var cut = context.Render<LoginForm>();
        await Login(cut);
        cut.WaitForElement("[data-testid=mfa-form]");
        transport.Step = null;
        const string shown = ".auth-feedback-slot[data-open=true] [data-testid=admission-error]";
        cut.Find("[data-testid=mfa-form] input").Input("111111");
        await cut.Find("[data-testid=mfa-form] form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(shown)));
        Assert.Empty(exit.Invocations);

        cut.Find("[data-testid=mfa-form] input").Input("222222");
        var retry = cut.Find("[data-testid=mfa-form] form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Single(exit.Invocations));
        Assert.Empty(cut.FindAll(shown));
        Assert.Equal(2, transport.Bodies.Count);
        Assert.Contains("Verifying", cut.Find("[data-testid=mfa-form] button[type=submit]").TextContent);
        exit.SetVoidResult();
        await retry;
        Assert.Equal(3, transport.Bodies.Count);
        Assert.Contains("222222", transport.Bodies[^1]);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(shown)));
    }

    // The recovery form shows its own error; the sign-in panel below it used to repeat the failure in other words.
    [Fact]
    public async Task A_refused_recovery_shows_one_error_that_closes_before_the_next_attempt()
    {
        using var transport = new Transport(true);
        await using var context = Context(transport, out _);
        var exit = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js")
            .SetupVoid("waitForClose", _ => true);
        var cut = context.Render<LoginForm>();
        cut.Find("[data-testid=login-help]").Click();
        cut.WaitForElement("[data-testid=login-recovery-choice]").Click();
        cut.WaitForElement("[data-testid=mfa-recovery-form]");
        const string shown = ".auth-feedback-slot[data-open=true] [data-testid=recovery-error]";
        await Recover(cut, "WRONG-CODE-1");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(shown)));
        Assert.Empty(cut.FindAll("[data-testid=admission-error]"));
        Assert.Single(cut.FindAll(".auth-error"));
        Assert.Empty(exit.Invocations);

        var retry = Recover(cut, "WRONG-CODE-2");
        cut.WaitForAssertion(() => Assert.Single(exit.Invocations));
        Assert.Single(transport.Bodies);
        exit.SetVoidResult();
        await retry;
        Assert.Equal(2, transport.Bodies.Count);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(shown)));
        Assert.Empty(cut.FindAll("[data-testid=admission-error]"));
    }

    // The setup step shows its own error; the sign-in panel below it used to repeat the failure in other words.
    [Fact]
    public async Task A_wrong_code_at_mandatory_setup_during_sign_in_shows_one_error()
    {
        using var transport = new Transport(true) { Step = AuthenticationStep.NewMfa };
        await using var context = Context(transport, out _);
        var cut = context.Render<LoginForm>();
        await Login(cut);
        cut.WaitForElement("[data-testid=enrollment-secret]");
        transport.Step = null;
        cut.Find("[data-testid=mfa-enrollment-form] input").Input("123456");
        await cut.Find("[data-testid=mfa-enrollment-form] form").SubmitAsync();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".auth-feedback-slot[data-open=true] [data-testid=mfa-error]")));
        Assert.Empty(cut.FindAll("[data-testid=admission-error]"));
        Assert.Single(cut.FindAll(".auth-error"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(" ", false)]
    [InlineData("/images/logo.png", true)]
    public async Task Logo_is_rendered_only_when_a_logo_path_is_set(string? logoPath, bool rendered)
    {
        using var transport = new Transport(false);
        await using var context = Context(transport, out _);
        context.Services.GetRequiredService<ShiftSoftware.ShiftIdentity.Dashboard.Blazor.ShiftIdentityDashboardBlazorOptions>().LogoPath = logoPath!;
        var cut = context.Render<LoginForm>();
        Assert.Equal(rendered, cut.FindAll("img.identity-login-logo").Count == 1);
        if (rendered) Assert.Equal(logoPath, cut.Find("img.identity-login-logo").GetAttribute("src"));
    }

    private static Task Login(IRenderedComponent<LoginForm> cut, string username = " \tSynthetic  User  ")
    {
        cut.FindAll("input")[0].Input(username);
        cut.FindAll("input")[1].Input("  Synthetic password 39!\t");
        return cut.Find("form").SubmitAsync();
    }

    private static Task Recover(IRenderedComponent<LoginForm> cut, string code)
    {
        var inputs = cut.FindAll("[data-testid=mfa-recovery-form] input");
        inputs[0].Input("synthetic");
        inputs[1].Input("Synthetic password 39!");
        inputs[2].Input(code);
        return cut.Find("[data-testid=mfa-recovery-form] form").SubmitAsync();
    }

    private static BunitContext Context(Transport transport, out RecordingStore store)
    {
        var context = new BunitContext();
        store = new();
        var http = new HttpClient(transport) { BaseAddress = new("https://identity.invalid/api/") };
        context.Services.AddSingleton(http); context.Services.AddSingleton(store.Session);
        context.Services.AddSingleton(new AuthenticationFlow(new HttpClient(transport) { BaseAddress = new("https://identity.invalid/") }, store.Session));
        context.Services.AddShiftBlazor(options => options.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid");
        context.Services.AddShiftIdentityDashboardBlazor(options => options.StagedAuthority = transport.Staged);
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        context.AddAuthorization(); context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    private sealed class Transport(bool staged) : HttpMessageHandler
    {
        public bool Staged => staged;
        public bool Success, Unavailable, Disconnected;
        public AuthenticationStep? Step;
        public Task? Wait;
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            if (Wait is not null) await Wait;
            if (Disconnected) throw new HttpRequestException("server-private-detail");
            if (request.RequestUri!.AbsolutePath.EndsWith("password-reset/request"))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create<AuthOutcome>(new SecurityDeliveryRequested()) };
            var status = Unavailable ? HttpStatusCode.ServiceUnavailable : Success || Step is not null ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
            if (staged)
            {
                AuthOutcome outcome = Unavailable ? new AuthenticationRefused(AuthenticationFailure.Unavailable)
                    : Success ? AuthenticationFlowTests.Session() : Step is { } step
                        ? new ChallengeRequired(new(step, step is AuthenticationStep.EmailVerification or AuthenticationStep.MfaRecovery ? null : "synthetic-operation",
                            DateTimeOffset.UtcNow.AddMinutes(5), step == AuthenticationStep.PasswordChange ? AuthenticationOperationPurpose.PasswordChange
                                : step == AuthenticationStep.NewMfa ? AuthenticationOperationPurpose.MfaEnrollment : AuthenticationOperationPurpose.Login,
                            step == AuthenticationStep.NewMfa ? new("JBSWY3DPEHPK3PXP", "otpauth://totp/synthetic",
                                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path d=\"M0 0h10v10z\"/></svg>") : null))
                        : new AuthenticationRefused(AuthenticationFailure.InvalidProof);
                return new(status) { Content = JsonContent.Create(outcome) };
            }
            return new(status) { Content = JsonContent.Create(Success ? new ShiftEntityResponse<TokenDTO>(AuthenticationFlowTests.Session().Session)
                : new ShiftEntityResponse<TokenDTO> { Message = new Message { Title = "server-private-detail", Body = "server-private-detail" } }) };
        }
    }
}
