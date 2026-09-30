using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Blazor;
using ShiftSoftware.ShiftIdentity.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Blazor.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.UserManager;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Services;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The production dashboard's account screens under the staged-authority switch: an ordinary session changes its
/// password and manages its authenticator through the staged flows, a forced step token from the deployed login
/// keeps the deployed route, and the profile names the authenticator action from the authoritative state. The
/// real components render against a scripted transport; the browser session is written only by a completed flow.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class DashboardStagedSecurityTests
{
    [Fact]
    public async Task Staged_password_change_proves_the_current_password_and_stores_only_the_new_session()
    {
        var store = new RecordingStore();
        var current = AuthenticationFlowTests.Session().Session;
        await store.Session.StoreTokenAsync(current);
        var transport = new DashboardTransport
        {
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route switch
            {
                "password-change" => Pending(AuthenticationStep.Password, "first"),
                "password-change/password" => Pending(AuthenticationStep.PasswordChange, "second"),
                "password-change/complete" => new PasswordChanged(AuthenticationFlowTests.Session()),
                _ => new AuthenticationRefused(AuthenticationFailure.InvalidRequest)
            })
        };
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render<ChangePasswordForm>();
        cut.WaitForAssertion(() => Assert.Contains("Confirm your current password", cut.Markup));
        var begin = Assert.Single(transport.Requests);
        Assert.Equal(("POST", "/api/identity/v2/password-change", "Bearer", current.Token), (begin.Method, begin.Path, begin.Scheme, begin.Credential));
        Assert.Single(store.Writes);

        cut.Find("input").Input("the current password"); await Submit(cut);
        cut.WaitForAssertion(() => Assert.Contains("Choose a new password", cut.Markup));
        var proof = transport.Requests[1];
        Assert.Equal(("/api/identity/v2/password-change/password", "Operation", "first"), (proof.Path, proof.Scheme, proof.Credential));
        Assert.Contains("\"currentPassword\":\"the current password\"", proof.Body);
        Assert.Single(store.Writes);

        cut.FindAll("input")[0].Input("A long new synthetic password"); cut.FindAll("input")[1].Input("A long new synthetic password");
        await Submit(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, store.Writes.Count));
        var complete = transport.Requests[2];
        Assert.Equal(("/api/identity/v2/password-change/complete", "Operation", "second"), (complete.Path, complete.Scheme, complete.Credential));
        Assert.Contains("\"newPassword\":\"A long new synthetic password\"", complete.Body);
        Assert.Equal(3, transport.Requests.Count);
        Assert.DoesNotContain(transport.Requests, x => x.Path.Contains("usermanager", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cancelling_a_staged_password_change_cancels_the_operation_and_returns_to_the_profile()
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route == "password-change" ? Pending(AuthenticationStep.Password, "first")
                : route == "operations/cancel" ? new OperationCancelled() : new AuthenticationRefused(AuthenticationFailure.InvalidRequest))
        };
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render<ChangePasswordForm>();
        cut.WaitForAssertion(() => Assert.Contains("Confirm your current password", cut.Markup));
        // The profile's change owns its box, whose close button cancels the operation.
        cut.Find("[data-testid=security-dialog-close]").Click();
        cut.WaitForAssertion(() => Assert.EndsWith("Identity/UserDataForm", context.Services.GetRequiredService<NavigationManager>().Uri));
        var cancel = transport.Requests.Last();
        Assert.Equal(("/api/identity/v2/operations/cancel", "Operation", "first"), (cancel.Path, cancel.Scheme, cancel.Credential));
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task A_forced_change_step_token_keeps_the_deployed_route_under_the_staged_switch()
    {
        var storage = new RecordingStore();
        var session = LegacySession(storage);
        await session.StoreTokenAsync(new TokenDTO
        {
            Token = "step-token", Flow = AuthPurpose.ChangePassword, TokenLifeTimeInSeconds = 300,
            UserData = new() { ID = "42", Username = "synthetic", FullName = "Synthetic User" }
        });
        var transport = new DashboardTransport();
        await using var context = Context(transport, session, staged: true);
        // The deployed login redirects here with the forced-change query; a query parameter is supplied by navigation.
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/ChangePasswordForm?Enforce=true");
        var cut = context.Render<ChangePasswordForm>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll("input").Count));
        Assert.Contains("You must change your password", cut.Markup);
        Assert.Empty(transport.Requests);
        var inputs = cut.FindAll("input");
        inputs[0].Change("the current password"); inputs[1].Change("A long new synthetic password"); inputs[2].Change("A long new synthetic password");
        await Submit(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, storage.Writes.Count));
        var put = Assert.Single(transport.Requests);
        Assert.Equal("PUT", put.Method); Assert.EndsWith("/usermanager/ChangePassword", put.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"currentPassword\":\"the current password\"", put.Body);
        Assert.Contains("\"newPassword\":\"A long new synthetic password\"", put.Body);
        Assert.Equal("legacy-session", storage.Writes.Last().Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staged_authenticator_setup_reads_the_status_and_asks_for_the_matching_proof(bool enrolled)
    {
        var store = new RecordingStore();
        var current = AuthenticationFlowTests.Session().Session;
        await store.Session.StoreTokenAsync(current);
        var purpose = enrolled ? AuthenticationOperationPurpose.MfaReplacement : AuthenticationOperationPurpose.MfaEnrollment;
        var transport = new DashboardTransport { Status = new AuthenticatorStatus(enrolled, false) };
        transport.Staged = (route, body) => Task.FromResult<AuthOutcome>(route switch
        {
            "mfa/start" => Pending(enrolled ? AuthenticationStep.ExistingMfa : AuthenticationStep.Password, "proof-handle", purpose),
            "mfa/password" or "mfa/existing" => Setup(purpose),
            "mfa/confirm" => new MfaChanged(AuthenticationFlowTests.Session()),
            _ => new AuthenticationRefused(AuthenticationFailure.InvalidRequest)
        });
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render<TotpEnrollmentForm>();
        cut.WaitForAssertion(() => Assert.Contains(enrolled ? "Replace your authenticator" : "Set up your authenticator", cut.Find("[data-testid=security-dialog-caption]").TextContent));
        Assert.Equal(("GET", "/api/identity/v2/mfa", "Bearer", current.Token), (transport.Requests[0].Method, transport.Requests[0].Path, transport.Requests[0].Scheme, transport.Requests[0].Credential));
        var start = transport.Requests[1];
        Assert.Equal(("/api/identity/v2/mfa/start", "Bearer", current.Token), (start.Path, start.Scheme, start.Credential));
        Assert.Contains(enrolled ? "\"replace\":true" : "\"replace\":false", start.Body);
        Assert.Empty(cut.FindAll("[data-testid=enrollment-secret]"));

        cut.Find("input").Input(enrolled ? "123456" : "the current password"); await Submit(cut);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=enrollment-secret]")));
        // The key is shown in blocks of four, as authenticator apps show it; they ignore the spaces.
        Assert.Equal("JBSW Y3DP EHPK 3PXP", cut.Find("[data-testid=enrollment-secret]").TextContent);
        var proof = transport.Requests[2];
        Assert.Equal(enrolled ? "/api/identity/v2/mfa/existing" : "/api/identity/v2/mfa/password", proof.Path);
        Assert.Equal(("Operation", "proof-handle"), (proof.Scheme, proof.Credential));
        Assert.Contains(enrolled ? "\"code\":\"123456\"" : "\"currentPassword\":\"the current password\"", proof.Body);
        Assert.Single(store.Writes);

        cut.Find("input").Input("654321"); await Submit(cut);
        cut.WaitForAssertion(() => Assert.Equal(2, store.Writes.Count));
        var confirm = transport.Requests[3];
        Assert.Equal(("/api/identity/v2/mfa/confirm", "Operation", "setup-handle"), (confirm.Path, confirm.Scheme, confirm.Credential));
        Assert.Contains("\"code\":\"654321\"", confirm.Body);
        Assert.DoesNotContain(transport.Requests, x => x.Path.Contains("usermanager", StringComparison.OrdinalIgnoreCase));
        // The change is confirmed before the form goes away.
        cut.WaitForAssertion(() => Assert.Contains(enrolled ? "Authenticator replaced" : "Authenticator set up", cut.Find("[data-testid=mfa-enrollment-done]").TextContent));
    }

    [Fact]
    public async Task Profile_password_change_opens_a_narrow_dialog_whose_steps_share_one_close_control_and_end_on_a_done_step()
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(true, false),
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route switch
            {
                "password-change" => Pending(AuthenticationStep.Password, "first"),
                "password-change/password" => Pending(AuthenticationStep.ExistingMfa, "second"),
                "password-change/mfa" => Pending(AuthenticationStep.PasswordChange, "third"),
                "password-change/complete" => new PasswordChanged(AuthenticationFlowTests.Session()),
                _ => new AuthenticationRefused(AuthenticationFailure.InvalidRequest)
            })
        };
        await using var context = Context(transport, store.Session, staged: true);
        var dialogs = context.Render<MudBlazor.MudDialogProvider>();
        var cut = context.Render<UserDataForm>();
        cut.WaitForElement("[data-testid=profile-change-password]").Click();
        dialogs.WaitForAssertion(() => Assert.Contains("Confirm your current password", dialogs.Markup));
        // A short security form, so a narrow dialog instead of the user's record-form width.
        Assert.Contains("mud-dialog-width-sm", dialogs.Find(".mud-dialog").ClassName);
        Assert.Equal("Change Password", dialogs.Find("[data-testid=security-dialog-caption]").TextContent);

        dialogs.Find("input").Input("the current password"); await Submit(dialogs);
        dialogs.WaitForElement("[data-testid=mfa-form]");
        // The code step is part of the dialog: no card of its own and no second cancel button.
        Assert.Empty(dialogs.FindAll(".mud-elevation-25"));
        Assert.DoesNotContain(dialogs.FindAll("button"), x => x.TextContent.Trim().Equals("Cancel", StringComparison.OrdinalIgnoreCase));
        Assert.Single(dialogs.FindAll("[data-testid=security-dialog-close]"));

        dialogs.Find("input").Input("123456"); await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("Choose a new password", dialogs.Markup));
        Assert.Equal(("/api/identity/v2/password-change/mfa", "second"), (transport.Requests[^1].Path, transport.Requests[^1].Credential));
        dialogs.FindAll("input")[0].Input("A long new synthetic password"); dialogs.FindAll("input")[1].Input("A long new synthetic password");
        await Submit(dialogs);
        dialogs.WaitForElement("[data-testid=password-change-done]");
        Assert.Equal(2, store.Writes.Count);
        dialogs.Find("[data-testid=password-change-finish]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
    }

    [Fact]
    public async Task Closing_the_profile_authenticator_dialog_cancels_the_started_setup_on_the_server()
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(false, false),
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route == "mfa/start"
                ? Pending(AuthenticationStep.Password, "proof-handle", AuthenticationOperationPurpose.MfaEnrollment)
                : route == "operations/cancel" ? new OperationCancelled() : new AuthenticationRefused(AuthenticationFailure.InvalidRequest))
        };
        await using var context = Context(transport, store.Session, staged: true);
        var dialogs = context.Render<MudBlazor.MudDialogProvider>();
        var cut = context.Render<UserDataForm>();
        cut.WaitForElement("[data-testid=profile-authenticator]").Click();
        dialogs.WaitForAssertion(() => Assert.Contains("Set up your authenticator", dialogs.Find("[data-testid=security-dialog-caption]").TextContent));
        dialogs.WaitForElement("[data-testid=mfa-enrollment-form] input");
        dialogs.Find("[data-testid=security-dialog-close]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        var cancel = transport.Requests.Single(x => x.Path.EndsWith("operations/cancel"));
        Assert.Equal(("Operation", "proof-handle"), (cancel.Scheme, cancel.Credential));
        Assert.Single(store.Writes);
    }

    // A second wrong entry used to leave the same error in place, so only the cleared field showed that anything had
    // happened. As at sign-in, the error now slides closed first, and the retry goes out once it has closed.
    [Theory]
    [InlineData("password")]
    [InlineData("setup")]
    [InlineData("replacement")]
    [InlineData("opt-out")]
    public async Task A_second_wrong_entry_in_a_profile_dialog_closes_the_error_before_the_retry_is_sent(string dialog)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(dialog is "replacement" or "opt-out", false),
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route switch
            {
                "password-change" => Pending(AuthenticationStep.Password, "first"),
                "mfa/start" => dialog == "replacement"
                    ? Pending(AuthenticationStep.ExistingMfa, "first", AuthenticationOperationPurpose.MfaReplacement)
                    : Pending(AuthenticationStep.Password, "first", AuthenticationOperationPurpose.MfaEnrollment),
                _ => new AuthenticationRefused(AuthenticationFailure.InvalidProof)
            })
        };
        await using var context = Context(transport, store.Session, staged: true);
        var exit = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js")
            .SetupVoid("waitForClose", _ => true);
        var dialogs = context.Render<MudBlazor.MudDialogProvider>();
        var cut = context.Render<UserDataForm>();
        cut.WaitForElement(dialog switch
        {
            "password" => "[data-testid=profile-change-password]",
            "opt-out" => "[data-testid=profile-mfa-opt-out]",
            _ => "[data-testid=profile-authenticator]"
        }).Click();
        dialogs.WaitForElement("form input");
        var proof = dialog switch
        {
            "password" => "/api/identity/v2/password-change/password",
            "setup" => "/api/identity/v2/mfa/password",
            "opt-out" => "/api/identity/v2/mfa/turn-off",
            _ => "/api/identity/v2/mfa/existing"
        };
        var error = dialog switch { "password" => "password-error", "opt-out" => "mfa-opt-out-error", _ => "mfa-error" };
        var shown = $".auth-feedback-slot[data-open=true] [data-testid={error}]";
        var (first, second) = dialog is "replacement" or "opt-out" ? ("111111", "222222") : ("a wrong password", "another wrong password");

        dialogs.Find("form input").Input(first); await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("not accepted", dialogs.Find(shown).TextContent));
        Assert.Empty(exit.Invocations);

        dialogs.Find("form input").Input(second);
        var retry = Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Single(exit.Invocations));
        Assert.Empty(dialogs.FindAll(".auth-feedback-slot[data-open=true]"));
        Assert.Single(transport.Requests, x => x.Path == proof);
        Assert.True(dialogs.Find("button[type=submit]").HasAttribute("disabled"));
        // Submitting again while the error closes sends nothing more.
        await Submit(dialogs);
        Assert.Single(transport.Requests, x => x.Path == proof);

        exit.SetVoidResult();
        await retry;
        Assert.Equal(2, transport.Requests.Count(x => x.Path == proof));
        Assert.Contains(second, transport.Requests.Last(x => x.Path == proof).Body);
        dialogs.WaitForAssertion(() => Assert.Contains("not accepted", dialogs.Find(shown).TextContent));
    }

    // A press that the form's own checks refuse (a code too short to send, or an empty field after a failure) used to
    // leave the previous error in place, so nothing seemed to happen. It now closes that error, sends nothing and puts
    // the focus on the field that says what to fix.
    [Theory]
    [InlineData("password", "")]
    [InlineData("setup", "")]
    [InlineData("opt-out", "527")]
    [InlineData("opt-out", "")]
    public async Task A_press_that_a_profile_dialog_refuses_closes_the_error_and_sends_nothing(string dialog, string entry)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(dialog == "opt-out", false),
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route switch
            {
                "password-change" => Pending(AuthenticationStep.Password, "first"),
                "mfa/start" => Pending(AuthenticationStep.Password, "first", AuthenticationOperationPurpose.MfaEnrollment),
                _ => new AuthenticationRefused(AuthenticationFailure.InvalidProof)
            })
        };
        await using var context = Context(transport, store.Session, staged: true);
        var module = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js");
        var close = module.SetupVoid("waitForClose", _ => true); close.SetVoidResult();
        var focus = module.SetupVoid("focusInvalid", _ => true); focus.SetVoidResult();
        var dialogs = context.Render<MudBlazor.MudDialogProvider>();
        var cut = context.Render<UserDataForm>();
        cut.WaitForElement(dialog switch
        {
            "password" => "[data-testid=profile-change-password]",
            "opt-out" => "[data-testid=profile-mfa-opt-out]",
            _ => "[data-testid=profile-authenticator]"
        }).Click();
        dialogs.WaitForElement("form input");
        var proof = dialog switch { "password" => "/api/identity/v2/password-change/password", "setup" => "/api/identity/v2/mfa/password", _ => "/api/identity/v2/mfa/turn-off" };
        var error = dialog switch { "password" => "password-error", "opt-out" => "mfa-opt-out-error", _ => "mfa-error" };
        dialogs.Find("form input").Input(dialog == "opt-out" ? "111111" : "a wrong password"); await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("not accepted", dialogs.Find($".auth-feedback-slot[data-open=true] [data-testid={error}]").TextContent));

        dialogs.Find("form input").Input(entry); await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll($"[data-testid={error}]")));
        Assert.Single(close.Invocations);
        Assert.Single(transport.Requests, x => x.Path == proof);
        Assert.Equal("true", dialogs.Find("form input").GetAttribute("aria-invalid"));
        Assert.Equal<object?>(dialogs.Find("form").Id, Assert.Single(focus.Invocations).Arguments.Single());
        Assert.False(dialogs.Find("button[type=submit]").HasAttribute("disabled"));
        Assert.Single(store.Writes);
    }

    // The administrator's link buttons answer below them. A second send used to leave the first answer in place.
    [Fact]
    public async Task A_second_send_from_the_user_form_closes_the_answer_before_it_is_sent_again()
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport { Staged = (_, _) => Task.FromResult<AuthOutcome>(new SecurityDeliveryRequested()) };
        await using var context = Context(transport, store.Session, staged: true);
        var close = context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/auth-feedback.js").SetupVoid("waitForClose", _ => true);
        var cut = context.Render<SecurityEmailAdminActions>(p => p.Add(x => x.UserKey, "encoded-target"));
        const string shown = ".auth-feedback-slot[data-open=true] [data-testid=security-email-admin-result]";
        await cut.FindAll("button")[0].ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Contains("check its inbox", cut.Find(shown).TextContent));
        Assert.Empty(close.Invocations);

        var again = cut.FindAll("button")[0].ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Single(close.Invocations));
        Assert.Empty(cut.FindAll(shown));
        Assert.Single(transport.Requests);
        Assert.All(cut.FindAll("button"), button => Assert.True(button.HasAttribute("disabled")));
        close.SetVoidResult();
        await again;
        Assert.Equal(2, transport.Requests.Count);
        cut.WaitForAssertion(() => Assert.Contains("check its inbox", cut.Find(shown).TextContent));
    }

    [Fact]
    public async Task Recovery_required_blocks_authenticator_setup_without_starting_an_operation()
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport { Status = new AuthenticatorStatus(false, true) };
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render<TotpEnrollmentForm>();
        cut.WaitForAssertion(() => Assert.Contains("Authenticator recovery is required", cut.Find("[data-testid=restricted-step]").TextContent));
        Assert.Single(transport.Requests);
        Assert.Equal("/api/identity/v2/mfa", transport.Requests[0].Path);
        Assert.Single(cut.FindAll("[data-testid=mfa-enrollment-close]"));
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task A_mandatory_enrollment_step_token_keeps_the_deployed_route_under_the_staged_switch()
    {
        var storage = new RecordingStore();
        var session = LegacySession(storage);
        await session.StoreTokenAsync(new TokenDTO
        {
            Token = "step-token", Flow = AuthPurpose.MfaEnrollment, TokenLifeTimeInSeconds = 300,
            UserData = new() { ID = "42", Username = "synthetic", FullName = "Synthetic User" }
        });
        var transport = new DashboardTransport();
        await using var context = Context(transport, session, staged: true);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("Identity/TotpEnrollmentForm?Enforce=true");
        var cut = context.Render<TotpEnrollmentForm>();
        cut.WaitForAssertion(() => Assert.Contains("legacy-secret", cut.Markup));
        var start = Assert.Single(transport.Requests);
        Assert.Equal("GET", start.Method); Assert.EndsWith("/usermanager/StartTotpEnrollment", start.Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You must set up two-factor authentication", cut.Markup);
    }

    [Theory]
    [InlineData("none", "Set up authenticator", false)]
    [InlineData("enrolled", "Replace authenticator", true)]
    [InlineData("mandatory", "Replace authenticator", false)]
    [InlineData("recovery", null, false)]
    [InlineData("unstaged", "Enroll Two Factor", false)]
    public async Task Profile_names_the_authenticator_action_from_the_staged_status(string scenario, string? label, bool optOut)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(scenario is "enrolled" or "mandatory", scenario == "recovery", Mandatory: scenario == "mandatory")
        };
        await using var context = Context(transport, store.Session, staged: scenario != "unstaged");
        var cut = context.Render<UserDataForm>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=profile-change-password]")));
        if (label is null)
        {
            cut.WaitForAssertion(() => Assert.Contains("Authenticator recovery is required", cut.Find("[data-testid=profile-authenticator-status]").TextContent));
            Assert.Empty(cut.FindAll("[data-testid=profile-authenticator]"));
        }
        else cut.WaitForAssertion(() => Assert.Contains(label, cut.Find("[data-testid=profile-authenticator]").TextContent));
        Assert.Equal(scenario != "unstaged", transport.Requests.Any(x => x.Path == "/api/identity/v2/mfa"));
        // Where MFA is optional, an enrolled user can also opt out of it.
        Assert.Equal(optOut, cut.FindAll("[data-testid=profile-mfa-opt-out]").Count == 1);
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task Opting_out_with_a_current_code_keeps_this_device_signed_in_and_the_profile_then_offers_setup()
    {
        var store = new RecordingStore();
        var current = AuthenticationFlowTests.Session().Session;
        await store.Session.StoreTokenAsync(current);
        var fresh = AuthenticationFlowTests.Session();
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(true, false),
            Staged = (route, _) => Task.FromResult<AuthOutcome>(route == "mfa/turn-off" ? new MfaChanged(fresh)
                : new AuthenticationRefused(AuthenticationFailure.InvalidRequest))
        };
        await using var context = Context(transport, store.Session, staged: true);
        var dialogs = context.Render<MudBlazor.MudDialogProvider>();
        var cut = context.Render<UserDataForm>();
        cut.WaitForElement("[data-testid=profile-mfa-opt-out]").Click();
        dialogs.WaitForElement("[data-testid=mfa-opt-out-form] input");
        Assert.Equal("Opt out of MFA", dialogs.Find("[data-testid=security-dialog-caption]").TextContent);
        Assert.Contains("mud-dialog-width-sm", dialogs.Find(".mud-dialog").ClassName);
        // Nothing starts on the server when the dialog opens: the code itself is the request.
        Assert.DoesNotContain(transport.Requests, x => x.Method == "POST");

        dialogs.Find("[data-testid=mfa-opt-out-form] input").Input("123456");
        transport.Status = new AuthenticatorStatus(false, false);
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains("MFA is off", dialogs.Find("[data-testid=mfa-opt-out-done]").TextContent));
        var request = Assert.Single(transport.Requests, x => x.Method == "POST");
        Assert.Equal(("/api/identity/v2/mfa/turn-off", "Bearer", current.Token), (request.Path, request.Scheme, request.Credential));
        Assert.Contains("123456", request.Body);
        // Every other session ended; this device continues with the fresh session that the answer carried.
        Assert.Equal(2, store.Writes.Count);
        Assert.Equal(fresh.Session.Token, store.Session.GetToken());

        dialogs.Find("[data-testid=mfa-opt-out-finish]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".mud-dialog")));
        cut.WaitForAssertion(() => Assert.Contains("Set up authenticator", cut.Find("[data-testid=profile-authenticator]").TextContent));
        Assert.Empty(cut.FindAll("[data-testid=profile-mfa-opt-out]"));
    }

    [Theory]
    [InlineData(AuthenticationFailure.ClientDenied, "required on this system")]
    [InlineData(AuthenticationFailure.AttemptsExhausted, "Too many attempts")]
    [InlineData(AuthenticationFailure.StaleOperation, "could not be confirmed")]
    public async Task A_refused_opt_out_says_why_and_keeps_the_session(AuthenticationFailure refusal, string message)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport
        {
            Status = new AuthenticatorStatus(true, false),
            Staged = (_, _) => Task.FromResult<AuthOutcome>(new AuthenticationRefused(refusal))
        };
        await using var context = Context(transport, store.Session, staged: true);
        var dialogs = context.Render<MudBlazor.MudDialogProvider>();
        var cut = context.Render<UserDataForm>();
        cut.WaitForElement("[data-testid=profile-mfa-opt-out]").Click();
        dialogs.WaitForElement("[data-testid=mfa-opt-out-form] input").Input("123456");
        await Submit(dialogs);
        dialogs.WaitForAssertion(() => Assert.Contains(message, dialogs.Find("[data-testid=mfa-opt-out-error]").TextContent));
        Assert.Empty(dialogs.FindAll("[data-testid=mfa-opt-out-done]"));
        Assert.Single(store.Writes);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("refused")]
    [InlineData("session")]
    [InlineData("network")]
    public async Task The_flow_status_read_returns_only_a_status_and_never_touches_the_store(string scenario)
    {
        var store = new RecordingStore();
        var transport = new DashboardTransport
        {
            Status = scenario switch
            {
                "refused" => new AuthenticationRefused(AuthenticationFailure.StaleOperation),
                "session" => AuthenticationFlowTests.Session(),
                _ => new AuthenticatorStatus(true, false)
            },
            Fail = scenario == "network"
        };
        var flow = new AuthenticationFlow(new StagedAuthorityHttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") }, store.Session);
        var result = await flow.ReadAuthenticatorAsync("current-access");
        switch (scenario)
        {
            case "status": Assert.True(Assert.IsType<AuthenticatorStatus>(result).Enrolled); break;
            case "refused": Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(result).Code); break;
            case "session": Assert.Equal(AuthenticationFailure.InvalidGrant, Assert.IsType<AuthenticationRefused>(result).Code); break;
            default: Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(result).Code); break;
        }
        var request = Assert.Single(transport.Requests);
        Assert.Equal(("GET", "/api/identity/v2/mfa", "Bearer", "current-access"), (request.Method, request.Path, request.Scheme, request.Credential));
        Assert.Empty(store.Writes); Assert.Null(flow.Pending); Assert.False(flow.Busy);
    }

    [Theory]
    [InlineData("https://identity.invalid/api/", "https://identity.invalid/")]
    [InlineData("https://identity.invalid/api", "https://identity.invalid/")]
    [InlineData("https://identity.invalid/API/", "https://identity.invalid/")]
    [InlineData("https://identity.invalid/prefix/api/", "https://identity.invalid/prefix/")]
    [InlineData("https://identity.invalid/", "https://identity.invalid/")]
    [InlineData("https://identity.invalid", "https://identity.invalid/")]
    public void The_staged_client_addresses_the_identity_api_root(string configured, string root) =>
        Assert.Equal(new Uri(root), StagedAuthorityHttpClient.ApiRootOf(configured));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Registration_adds_the_staged_flow_only_when_the_switch_is_on(bool staged)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new RecordingStore().Session);
        services.AddSingleton(new ShiftIdentityBlazorOptions("synthetic-app", "https://identity.invalid/api/", "https://identity.invalid/", false));
        services.AddShiftIdentityDashboardBlazor(o => o.StagedAuthority = staged);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        if (!staged)
        {
            Assert.Null(scope.ServiceProvider.GetService<AuthenticationFlow>());
            Assert.Null(scope.ServiceProvider.GetService<StagedAuthorityHttpClient>());
            return;
        }
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AuthenticationFlow>());
        Assert.Equal(new Uri("https://identity.invalid/"), scope.ServiceProvider.GetRequiredService<StagedAuthorityHttpClient>().BaseAddress);
        Assert.Same(scope.ServiceProvider.GetRequiredService<AuthenticationFlow>(), scope.ServiceProvider.GetRequiredService<AuthenticationFlow>());
    }

    // ── fixtures ─────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reset_return_keeps_explicit_login_visible_under_the_dashboard_switch_only(bool staged)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        await using var context = Context(new DashboardTransport(), store.Session, staged);
        var nav = context.Services.GetRequiredService<NavigationManager>(); nav.NavigateTo(SecurityLinkNavigation.LoginAfterReset);
        var cut = context.Render<Microsoft.AspNetCore.Components.Authorization.CascadingAuthenticationState>(p => p
            .AddChildContent<ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.Auth.LoginForm>());
        if (staged)
        {
            cut.WaitForAssertion(() => Assert.Contains("Password reset. Sign in with your new password.", cut.Markup));
            Assert.EndsWith(SecurityLinkNavigation.LoginAfterReset, nav.Uri);
        }
        else cut.WaitForAssertion(() => Assert.Equal("http://localhost/", nav.Uri));
        Assert.Single(store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployed_public_request_pages_use_the_flow_even_with_an_unrelated_browser_session(bool verification)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport { Staged = (_, _) => Task.FromResult<AuthOutcome>(new SecurityDeliveryRequested()) };
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render(builder => { builder.OpenComponent(0, verification ? typeof(SendEmailVerificationLink) : typeof(SendResetPasswordLink)); builder.CloseComponent(); });
        cut.Find("input").Input("saved-user");
        await FormSubmission.SubmitAsync(cut);
        cut.WaitForAssertion(() => Assert.Contains("Check your inbox", cut.Markup));
        var request = Assert.Single(transport.Requests);
        Assert.Equal("/api/identity/v2/" + (verification ? "email-verification" : "password-reset") + "/request", request.Path);
        Assert.Null(request.Scheme); Assert.Contains("saved-user", request.Body);
        Assert.Single(store.Writes);
        Assert.DoesNotContain("Identity/login", context.Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployed_link_pages_open_inertly_and_complete_without_changing_the_browser_session(bool verification)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var purpose = verification ? AuthenticationOperationPurpose.EmailVerify : AuthenticationOperationPurpose.PasswordResetEmail;
        var transport = new DashboardTransport { Staged = (route, _) => Task.FromResult<AuthOutcome>(route.EndsWith("/open")
            ? new SecurityLinkOpened("protected-page", "saved@example.invalid", purpose, DateTimeOffset.UtcNow.AddMinutes(5))
            : verification ? new EmailVerificationCompleted("https://hub.example.invalid/home") : new ReturnToLogin()) };
        await using var context = Context(transport, store.Session, staged: true);
        context.JSInterop.SetupModule("./_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/security-link.js")
            .Setup<bool>("clearFragment", _ => true).SetResult(true);
        var nav = context.Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("Identity/" + (verification ? "VerifyEmail" : "ResetPassword") + "?returnUrl=https://ignored.example.invalid/#grant=opaque&purpose=" + purpose);
        var cut = context.Render(builder => { builder.OpenComponent(0, verification ? typeof(VerifyEmail) : typeof(ResetPassword)); builder.CloseComponent(); });
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=security-link-target]")));
        Assert.Single(transport.Requests); Assert.EndsWith("/security-link/open", transport.Requests[0].Path);
        Assert.DoesNotContain("hub.example.invalid", nav.Uri);
        if (verification) cut.FindAll("button").Single(x => x.TextContent.Trim() == "Verify my email").Click();
        else
        {
            foreach (var input in cut.FindAll("input")) input.Input("A new synthetic phrase 89!");
            await FormSubmission.SubmitAsync(cut);
        }
        cut.WaitForAssertion(() => Assert.Equal(2, transport.Requests.Count));
        Assert.Contains("protected-page", transport.Requests[1].Body);
        cut.WaitForAssertion(() => Assert.Equal(verification ? "https://hub.example.invalid/home" : "http://localhost/Identity/login?securityLink=reset", nav.Uri));
        Assert.All(transport.Requests, x => Assert.Null(x.Scheme)); Assert.Single(store.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrator_link_actions_use_the_saved_encoded_key_and_an_explicit_bearer(bool verification)
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport { Staged = (_, _) => Task.FromResult<AuthOutcome>(new SecurityDeliveryRequested()) };
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render<SecurityEmailAdminActions>(p => p.Add(x => x.UserKey, "encoded-target"));
        cut.FindAll("button")[verification ? 1 : 0].Click();
        cut.WaitForAssertion(() => Assert.Contains("check its inbox", cut.Markup));
        var request = Assert.Single(transport.Requests);
        Assert.Equal("/api/identity/v2/" + (verification ? "email-verification" : "password-reset") + "/admin", request.Path);
        Assert.Equal("Bearer", request.Scheme); Assert.Contains("encoded-target", request.Body);
        Assert.Single(store.Writes);
    }

    [Fact]
    public async Task Profile_verification_uses_current_account_authority_and_reports_unconfirmed_delivery()
    {
        var store = new RecordingStore();
        await store.Session.StoreTokenAsync(AuthenticationFlowTests.Session().Session);
        var transport = new DashboardTransport { EmailVerified = false, Staged = (_, _) => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.Unavailable)) };
        await using var context = Context(transport, store.Session, staged: true);
        var cut = context.Render<UserDataForm>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("button").Where(x => x.TextContent.Trim() == "Send Email Verification")));
        cut.FindAll("button").Single(x => x.TextContent.Trim() == "Send Email Verification").Click();
        cut.WaitForAssertion(() => Assert.Contains(transport.Requests, x => x.Path.EndsWith("email-verification/request-current")));
        var request = Assert.Single(transport.Requests, x => x.Path.EndsWith("email-verification/request-current"));
        Assert.Equal("Bearer", request.Scheme);
        Assert.DoesNotContain(transport.Requests, x => x.Path.Contains("SendEmailVerificationLink", StringComparison.OrdinalIgnoreCase));
        Assert.Single(store.Writes);
    }

    private static BunitContext Context(DashboardTransport transport, IdentitySession session, bool staged)
    {
        var context = new BunitContext();
        // The dashboard's ordinary client and the staged flow's raw client share one scripted transport.
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddSingleton(session);
        context.Services.AddSingleton(new StagedAuthorityHttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(o => o.StagedAuthority = staged);
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }

    /// <summary>The production session registration (legacy renewal transport) over a recording storage: it holds step tokens as the deployed login stores them.</summary>
    private static IdentitySession LegacySession(RecordingStore storage)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIdentityTokenStorage>(storage);
        services.AddShiftIdentity("synthetic-app", "https://identity.invalid/api/", "https://identity.invalid/");
        return services.BuildServiceProvider().GetRequiredService<IdentitySession>();
    }

    private static ChallengeRequired Pending(AuthenticationStep step, string handle, AuthenticationOperationPurpose purpose = AuthenticationOperationPurpose.PasswordChange) =>
        new(new(step, handle, DateTimeOffset.UtcNow.AddMinutes(5), purpose));
    private static ChallengeRequired Setup(AuthenticationOperationPurpose purpose) => new(new(AuthenticationStep.NewMfa, "setup-handle", DateTimeOffset.UtcNow.AddMinutes(5), purpose,
        new("JBSWY3DPEHPK3PXP", "otpauth://totp/synthetic", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"><path d=\"M0 0h10v10z\"/></svg>")));
    private static Task Submit<T>(IRenderedComponent<T> cut) where T : class, IComponent => FormSubmission.SubmitAsync(cut);

    /// <summary>Scripts the staged routes, the deployed step routes and the profile read; records every request.</summary>
    private sealed class DashboardTransport : HttpMessageHandler
    {
        public AuthOutcome Status { get; set; } = new AuthenticatorStatus(false, false);
        public bool Fail { get; set; }
        public bool EmailVerified { get; set; } = true;
        public Func<string, string, Task<AuthOutcome>> Staged { get; set; } = (_, _) => Task.FromResult<AuthOutcome>(new AuthenticationRefused(AuthenticationFailure.InvalidRequest));
        public List<(string Method, string Path, string? Scheme, string? Credential, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.Method.Method, path, request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter, body));
            if (Fail) throw new HttpRequestException("Synthetic transport failure.");
            if (path.StartsWith("/api/identity/v2/", StringComparison.Ordinal))
            {
                var outcome = request.Method == HttpMethod.Get && path == "/api/identity/v2/mfa" ? Status : await Staged(path["/api/identity/v2/".Length..], body);
                return new(outcome is AuthenticationRefused ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = JsonContent.Create<AuthOutcome>(outcome) };
            }
            if (request.Method == HttpMethod.Put && path.EndsWith("/usermanager/ChangePassword", StringComparison.OrdinalIgnoreCase))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<TokenDTO>(new TokenDTO
                {
                    Token = "legacy-session", RefreshToken = "legacy-refresh", TokenLifeTimeInSeconds = 900,
                    UserData = new() { ID = "42", Username = "synthetic", FullName = "Synthetic User" }
                })) };
            if (request.Method == HttpMethod.Get && path.EndsWith("/usermanager/StartTotpEnrollment", StringComparison.OrdinalIgnoreCase))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager.TotpDTO>(new()
                {
                    Secret = "legacy-secret", Uri = "otpauth://totp/synthetic", Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\"></svg>",
                    SasToken = "signed", Expires = "0"
                })) };
            if (request.Method == HttpMethod.Get && path.StartsWith("/UserManager/UserData", StringComparison.OrdinalIgnoreCase))
                return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<UserDataDTO>(new UserDataDTO
                {
                    ID = "42", Username = "synthetic", FullName = "Synthetic User", Email = "saved@example.invalid", EmailVerified = EmailVerified
                })) };
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
