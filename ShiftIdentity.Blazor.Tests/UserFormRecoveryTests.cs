using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// Lost-device recovery and turning MFA off on the production user screens under the identity authority. Before
/// this, the recovery code could be issued only from the development app's account panel: a host's administrator
/// could reset an authenticator from the user list, but had no screen to issue the code that ends the reset, and
/// the user form showed the reset account as "Not set up". The bulk reset is gone under the authority: where MFA is
/// optional, the user form turns it off per user, with the guard of a recovery code. The real components render
/// against a scripted transport.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class UserFormRecoveryTests
{
    private const string RecoveryOperator = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"],\"ManageMfaRecovery\":[\"m\"]}}";
    private const string UsersOnly = "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"]}}";

    [Theory]
    [InlineData(false, true, "Enabled")]
    [InlineData(false, false, "Not set up")]
    [InlineData(true, false, "Recovery required")]
    public async Task The_authenticator_status_names_recovery_as_its_own_state(bool recovery, bool enabled, string status)
    {
        var transport = new RecoveryTransport { Target = Target(recovery, enabled) };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForAssertion(() => Assert.Contains(status, cut.Find("[data-testid=user-authenticator-status]").TextContent));
        Assert.Equal(recovery, cut.FindAll("[data-testid=user-authenticator-recovery-help]").Count == 1);
    }

    [Theory]
    [InlineData("recovery operator", true)]
    [InlineData("users write only", false)]
    [InlineData("own account", false)]
    [InlineData("without the authority", false)]
    public async Task Recover_authenticator_is_offered_to_a_recovery_operator_for_another_account_under_the_authority(string scenario, bool offered)
    {
        var transport = new RecoveryTransport { Target = Target(recovery: true, enabled: false) };
        await using var context = await Context(transport, staged: scenario != "without the authority",
            scenario == "users write only" ? UsersOnly : RecoveryOperator, subject: scenario == "own account" ? "42" : "operator");
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForAssertion(() => Assert.Contains("Recovery required", cut.Find("[data-testid=user-authenticator-status]").TextContent));
        Assert.Equal(offered, cut.FindAll("[data-testid=user-recover-authenticator]").Count == 1);
    }

    [Fact]
    public async Task A_recovery_operator_issues_the_code_by_the_saved_key_and_the_form_then_shows_recovery_required()
    {
        var transport = new RecoveryTransport { Target = Target(recovery: false, enabled: true) };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement("[data-testid=user-recover-authenticator]").Click();

        var form = dialogs.WaitForElement("[data-testid=mfa-recovery-admin-form]");
        Assert.Equal("Recover authenticator", dialogs.Find("[data-testid=security-dialog-caption]").TextContent);
        Assert.Contains("Saved user (synthetic-target)", dialogs.Find("[data-testid=recovery-target]").TextContent);
        // The dialog is narrow: the step is a short form, not a record form.
        Assert.Contains("mud-dialog-width-sm", dialogs.Find(".mud-dialog").ClassName);
        dialogs.Find("[data-testid=mfa-recovery-admin-form] input").Input("Ticket 1234, identity checked by phone");
        await Submit(dialogs);

        dialogs.WaitForAssertion(() => Assert.Equal("SYNTHETIC-RECOVERY-CODE", dialogs.Find("[data-testid=recovery-code]").TextContent));
        var issue = Assert.Single(transport.Issues);
        Assert.Equal(("Bearer", ConfirmationSession.Token), (issue.Scheme, issue.Credential));
        using (var body = JsonDocument.Parse(issue.Body))
        {
            Assert.Equal("42", body.RootElement.GetProperty("userKey").GetString());
            Assert.Equal(0, body.RootElement.GetProperty("userID").GetInt64());
            Assert.Equal("Ticket 1234, identity checked by phone", body.RootElement.GetProperty("verificationReference").GetString());
        }
        Assert.Equal(3, dialogs.FindAll("[data-testid=recovery-next-steps] li").Count);

        dialogs.Find("[data-testid=recovery-done]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        cut.WaitForAssertion(() => Assert.Contains("Recovery required", cut.Find("[data-testid=user-authenticator-status]").TextContent));
        Assert.Single(cut.FindAll("[data-testid=user-authenticator-recovery-help]"));
    }

    [Fact]
    public async Task A_refused_issue_explains_the_permission_and_shows_no_code()
    {
        var transport = new RecoveryTransport { Target = Target(recovery: true, enabled: false), Refusal = AuthenticationFailure.ClientDenied };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement("[data-testid=user-recover-authenticator]").Click();
        dialogs.WaitForElement("[data-testid=mfa-recovery-admin-form] input").Input("Ticket 1234");
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("Manage MFA Recovery permission", dialogs.Find("[data-testid=recovery-admin-error]").TextContent));
        Assert.Empty(dialogs.FindAll("[data-testid=recovery-code]"));
        dialogs.Find("[data-testid=security-dialog-close]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        Assert.Contains("Recovery required", cut.Find("[data-testid=user-authenticator-status]").TextContent);
    }

    [Fact]
    public async Task A_second_refused_issue_closes_the_error_before_it_is_sent_again()
    {
        var transport = new RecoveryTransport { Target = Target(recovery: false, enabled: true), Refusal = AuthenticationFailure.StaleOperation };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var exit = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js")
            .SetupVoid("waitForClose", _ => true);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement("[data-testid=user-recover-authenticator]").Click();
        dialogs.WaitForElement("[data-testid=mfa-recovery-admin-form] input").Input("Ticket 1234");
        Task Issue() => Submit(dialogs);
        const string shown = ".auth-feedback-slot[data-open=true] [data-testid=recovery-admin-error]";
        await Issue();
        dialogs.WaitForAssertion(() => Assert.Contains("This account changed meanwhile", dialogs.Find(shown).TextContent));

        var retry = Issue();
        dialogs.WaitForAssertion(() => Assert.Single(exit.Invocations));
        Assert.Empty(dialogs.FindAll(".auth-feedback-slot[data-open=true]"));
        Assert.Single(transport.Issues);
        exit.SetVoidResult();
        await retry;
        Assert.Equal(2, transport.Issues.Count);
        dialogs.WaitForAssertion(() => Assert.Contains("This account changed meanwhile", dialogs.Find(shown).TextContent));
    }

    // The bulk reset locked every selected user out of password sign-in until a recovery code, with neither the
    // recovery permission nor a note behind it. Under the authority the list no longer offers it; without the
    // authority it keeps its deployed behaviour, which leaves the user with the password only.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_user_list_offers_the_bulk_authenticator_reset_only_without_the_authority(bool staged)
    {
        var transport = new RecoveryTransport { Target = Target(false, true) };
        await using var context = await Context(transport, staged, RecoveryOperator);
        var cut = context.Render<UserList>();
        cut.WaitForAssertion(() => Assert.Contains(cut.FindComponents<ActionButton<UserListDTO>>(), x => x.Instance.Endpoint!.EndsWith("VerifyPhones")));
        Assert.Equal(!staged, cut.FindComponents<ActionButton<UserListDTO>>().Any(x => x.Instance.Endpoint!.EndsWith("ResetTotp")));
        Assert.Equal(!staged, cut.Markup.Contains("Reset TOTP"));
    }

    [Theory]
    [InlineData("enabled", true)]
    [InlineData("recovery required", true)]
    [InlineData("not set up", false)]
    [InlineData("mandatory", false)]
    [InlineData("policy unknown", false)]
    [InlineData("users write only", false)]
    [InlineData("own account", false)]
    [InlineData("without the authority", false)]
    public async Task Turn_off_MFA_is_offered_where_MFA_is_optional_for_another_account_that_has_it(string scenario, bool offered)
    {
        var transport = new RecoveryTransport
        {
            Target = Target(recovery: scenario == "recovery required", enabled: scenario is not ("recovery required" or "not set up")),
            Policy = scenario switch
            {
                "mandatory" => new AuthenticatorStatus(true, false, Mandatory: true),
                "policy unknown" => new AuthenticationRefused(AuthenticationFailure.Unavailable),
                _ => new AuthenticatorStatus(true, false)
            }
        };
        await using var context = await Context(transport, staged: scenario != "without the authority",
            scenario == "users write only" ? UsersOnly : RecoveryOperator, subject: scenario == "own account" ? "42" : "operator");
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=user-authenticator-status]")));
        // The policy is read once, with the operator's own session, and only for an operator who could act on it.
        var read = scenario is not ("users write only" or "without the authority");
        cut.WaitForAssertion(() => Assert.Equal(read ? 1 : 0, transport.PolicyReads.Count));
        if (read) Assert.Equal(("Bearer", ConfirmationSession.Token), Assert.Single(transport.PolicyReads));
        cut.Render();
        Assert.Equal(offered, cut.FindAll("[data-testid=user-turn-off-mfa]").Count == 1);
    }

    [Fact]
    public async Task An_operator_turns_off_MFA_by_the_saved_key_with_a_note_and_the_form_then_shows_no_authenticator()
    {
        var transport = new RecoveryTransport { Target = Target(recovery: true, enabled: false) };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement("[data-testid=user-turn-off-mfa]").Click();

        dialogs.WaitForElement("[data-testid=mfa-turn-off-form]");
        Assert.Equal("Turn off MFA", dialogs.Find("[data-testid=security-dialog-caption]").TextContent);
        Assert.Contains("Saved user (synthetic-target)", dialogs.Find("[data-testid=turn-off-target]").TextContent);
        Assert.Contains("mud-dialog-width-sm", dialogs.Find(".mud-dialog").ClassName);
        // A lost phone is recovery's case, and a user who waits for recovery is told that this ends it.
        Assert.Contains("use Recover authenticator instead", dialogs.Markup);
        Assert.Single(dialogs.FindAll("[data-testid=turn-off-ends-recovery]"));
        dialogs.Find("[data-testid=mfa-turn-off-form] input").Input("Ticket 77, the user asked by phone");
        await Submit(dialogs);

        dialogs.WaitForAssertion(() => Assert.Contains("MFA turned off", dialogs.Find("[data-testid=mfa-turn-off-done]").TextContent));
        var turnOff = Assert.Single(transport.TurnOffs);
        Assert.Equal(("Bearer", ConfirmationSession.Token), (turnOff.Scheme, turnOff.Credential));
        using (var body = JsonDocument.Parse(turnOff.Body))
        {
            Assert.Equal("42", body.RootElement.GetProperty("userKey").GetString());
            Assert.Equal(0, body.RootElement.GetProperty("userID").GetInt64());
            Assert.Equal("Ticket 77, the user asked by phone", body.RootElement.GetProperty("verificationReference").GetString());
        }
        Assert.Empty(transport.Issues);

        dialogs.Find("[data-testid=mfa-turn-off-finish]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        cut.WaitForAssertion(() => Assert.Contains("Not set up", cut.Find("[data-testid=user-authenticator-status]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=user-authenticator-recovery-help]"));
        Assert.Empty(cut.FindAll("[data-testid=user-turn-off-mfa]"));
    }

    [Fact]
    public async Task A_refused_turn_off_explains_why_and_leaves_the_form_as_it_was()
    {
        var transport = new RecoveryTransport { Target = Target(recovery: false, enabled: true), Refusal = AuthenticationFailure.ClientDenied };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement("[data-testid=user-turn-off-mfa]").Click();
        dialogs.WaitForElement("[data-testid=mfa-turn-off-form] input").Input("Ticket 78");
        // Nothing to end: this user does not wait for recovery.
        Assert.Empty(dialogs.FindAll("[data-testid=turn-off-ends-recovery]"));
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("mandatory on this system", dialogs.Find("[data-testid=mfa-turn-off-error]").TextContent));
        Assert.Empty(dialogs.FindAll("[data-testid=mfa-turn-off-done]"));
        dialogs.Find("[data-testid=security-dialog-close]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        Assert.Contains("Enabled", cut.Find("[data-testid=user-authenticator-status]").TextContent);
        Assert.Single(cut.FindAll("[data-testid=user-turn-off-mfa]"));
    }

    [Fact]
    public async Task A_second_refused_turn_off_closes_the_error_before_it_is_sent_again()
    {
        var transport = new RecoveryTransport { Target = Target(recovery: false, enabled: true), Refusal = AuthenticationFailure.StaleOperation };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var exit = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js")
            .SetupVoid("waitForClose", _ => true);
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement("[data-testid=user-turn-off-mfa]").Click();
        dialogs.WaitForElement("[data-testid=mfa-turn-off-form] input").Input("Ticket 79");
        const string shown = ".auth-feedback-slot[data-open=true] [data-testid=mfa-turn-off-error]";
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("This account changed meanwhile", dialogs.Find(shown).TextContent));

        var retry = Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Single(exit.Invocations));
        Assert.Empty(dialogs.FindAll(".auth-feedback-slot[data-open=true]"));
        Assert.Single(transport.TurnOffs);
        exit.SetVoidResult();
        await retry;
        Assert.Equal(2, transport.TurnOffs.Count);
        dialogs.WaitForAssertion(() => Assert.Contains("This account changed meanwhile", dialogs.Find(shown).TextContent));
    }

    // A note that the dialog refuses (shorter than three characters) closes the previous refusal and sends nothing.
    [Theory]
    [InlineData("recovery")]
    [InlineData("turn-off")]
    public async Task A_note_the_dialog_refuses_closes_the_error_and_sends_nothing(string dialog)
    {
        var transport = new RecoveryTransport { Target = Target(recovery: false, enabled: true), Refusal = AuthenticationFailure.StaleOperation };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var module = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js");
        var close = module.SetupVoid("waitForClose", _ => true); close.SetVoidResult();
        var focus = module.SetupVoid("focusInvalid", _ => true); focus.SetVoidResult();
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<UserForm>(p => p.Add(x => x.Key, "42"));
        cut.WaitForElement(dialog == "recovery" ? "[data-testid=user-recover-authenticator]" : "[data-testid=user-turn-off-mfa]").Click();
        var error = dialog == "recovery" ? "recovery-admin-error" : "mfa-turn-off-error";
        dialogs.WaitForElement("form input").Input("Ticket 81");
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("This account changed meanwhile", dialogs.Find($"[data-testid={error}]").TextContent));

        dialogs.Find("form input").Input("ab");
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll($"[data-testid={error}]")));
        Assert.Single(close.Invocations);
        Assert.Equal(1, dialog == "recovery" ? transport.Issues.Count : transport.TurnOffs.Count);
        Assert.Equal("true", dialogs.Find("form input").GetAttribute("aria-invalid"));
        Assert.Single(focus.Invocations);
    }

    // Like every administrator route, a refusal that asks for a recent sign-in opens the confirmation once, and the
    // same request then goes out again with the confirmed session.
    [Fact]
    public async Task Turning_off_MFA_asks_for_a_recent_sign_in_like_every_administrator_action()
    {
        var storage = new RecordingStore(); await storage.Session.StoreTokenAsync(ConfirmationSession);
        var confirmed = AdministratorConfirmationUiTests.Session();
        var confirmation = new Confirmation(async () => { await storage.Session.StoreTokenAsync(confirmed); return confirmed.Token; });
        var continuation = new AdministratorActionContinuation(storage.Session, confirmation, new("https://identity.invalid/"));
        var sent = new List<(string? Credential, string Body)>();
        async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
        {
            sent.Add((request.Headers.Authorization?.Parameter, await request.Content!.ReadAsStringAsync(ct)));
            AuthOutcome outcome = sent.Count == 1 ? new AuthenticationRefused(AuthenticationFailure.ReauthenticationRequired)
                : new AdminAccountChanged(AdminAccountChange.Mfa, true, 2);
            return new(sent.Count == 1 ? HttpStatusCode.Forbidden : HttpStatusCode.OK) { Content = JsonContent.Create(outcome) };
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://identity.invalid/api/identity/v2/admin/mfa/turn-off")
        {
            Content = JsonContent.Create(new AdminMfaTurnOffRequest(0, "Ticket 80", "42"))
        };
        request.Headers.Authorization = new("Bearer", ConfirmationSession.Token);
        using var result = await continuation.SendAsync(request, Send, CancellationToken.None);
        Assert.Equal(1, confirmation.Calls);
        Assert.Equal([ConfirmationSession.Token, confirmed.Token], sent.Select(x => x.Credential));
        Assert.Equal(sent[0].Body, sent[1].Body);
        Assert.IsType<AdminAccountChanged>(await result.Content.ReadFromJsonAsync<AuthOutcome>());
    }

    [Fact]
    public async Task The_user_list_marks_an_account_that_waits_for_recovery()
    {
        var transport = new RecoveryTransport { Target = Target(false, false), ListRecovery = true };
        await using var context = await Context(transport, staged: true, RecoveryOperator);
        var cut = context.Render<UserList>();
        cut.WaitForAssertion(() => Assert.Contains("Recovery required: this user cannot sign in until they recover their authenticator app", cut.Markup));
    }

    private static UserDTO Target(bool recovery, bool enabled) => new()
    {
        ID = "42", Username = "synthetic-target", FullName = "Saved user", IsActive = true, AccessTree = "{}",
        TotpEnabled = enabled, MfaRecoveryRequired = recovery,
        CompanyBranchID = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" }
    };

    private static readonly ShiftSoftware.ShiftIdentity.Core.DTOs.TokenDTO ConfirmationSession = AdministratorConfirmationUiTests.Session();

    private static Task Submit(IRenderedComponent<MudDialogProvider> dialogs) => FormSubmission.SubmitAsync(dialogs);

    private sealed class Confirmation(Func<Task<string?>> confirm) : IAdministratorConfirmation
    {
        public int Calls { get; private set; }
        public Task<string?> ConfirmAsync(string currentAccess, CancellationToken cancellationToken) { Calls++; return confirm(); }
    }

    private static async Task<BunitContext> Context(RecoveryTransport transport, bool staged, string tree, string subject = "operator")
    {
        var storage = new RecordingStore(); await storage.Session.StoreTokenAsync(ConfirmationSession);
        var context = new BunitContext();
        context.Services.AddSingleton(storage.Session);
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/api/") });
        context.Services.AddSingleton(new StagedAuthorityHttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/api/");
        context.Services.AddShiftIdentityDashboardBlazor(o => o.StagedAuthority = staged);
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim("sub", subject), new Claim(TypeAuthClaimTypes.AccessTree, tree));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    /// <summary>
    /// Serves one saved user, its list page and the operator's authenticator status (the host's MFA policy), scripts
    /// the recovery-code and turn-off routes, and records their requests.
    /// </summary>
    private sealed class RecoveryTransport : HttpMessageHandler
    {
        public UserDTO Target { get; init; } = new();
        public AuthenticationFailure? Refusal { get; init; }
        public AuthOutcome Policy { get; init; } = new AuthenticatorStatus(true, false);
        public bool ListRecovery { get; init; }
        public List<(string? Scheme, string? Credential, string Body)> Issues { get; } = [];
        public List<(string? Scheme, string? Credential, string Body)> TurnOffs { get; } = [];
        public List<(string? Scheme, string? Credential)> PolicyReads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/identity/v2/mfa/recovery-code")
            {
                Issues.Add((request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter, await request.Content!.ReadAsStringAsync(ct)));
                AuthOutcome outcome = Refusal is { } code ? new AuthenticationRefused(code)
                    : new MfaRecoveryCodeIssued("SYNTHETIC-RECOVERY-CODE", DateTimeOffset.UtcNow.AddMinutes(15));
                return new(Refusal is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = JsonContent.Create<AuthOutcome>(outcome) };
            }
            if (path == "/api/identity/v2/admin/mfa/turn-off")
            {
                TurnOffs.Add((request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter, await request.Content!.ReadAsStringAsync(ct)));
                AuthOutcome outcome = Refusal is { } code ? new AuthenticationRefused(code) : new AdminAccountChanged(AdminAccountChange.Mfa, true, 2);
                return new(Refusal is null ? HttpStatusCode.OK : HttpStatusCode.BadRequest) { Content = JsonContent.Create<AuthOutcome>(outcome) };
            }
            if (path == "/api/identity/v2/mfa" && request.Method == HttpMethod.Get)
            {
                PolicyReads.Add((request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter));
                return new(Policy is AuthenticationRefused ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = JsonContent.Create(Policy) };
            }
            if (path == "/api/IdentityUser/42" && request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<UserDTO>(Target)) };
            if (path == "/api/IdentityUser" && request.Method == HttpMethod.Get && ListRecovery)
                return new(HttpStatusCode.OK) { Content = new StringContent(
                    "{\"@odata.count\":1,\"value\":[{\"ID\":\"42\",\"FullName\":\"Saved user\",\"Username\":\"synthetic-target\",\"IsActive\":true,\"TotpEnabled\":false,\"MfaRecoveryRequired\":true}]}",
                    System.Text.Encoding.UTF8, "application/json") };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
