using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Device sign-in (RFC 8628) on the authority's real routes and SQL store: a device asks for codes and polls; on a phone
/// that never signs in, a person looks the code up and types the username and password of an account that allows
/// device sign-in, or denies the code; the device receives an ordinary session of that account.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class DeviceAuthorizationSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string Screen = "service-screen";
    private static readonly DeviceGrantOptions Grant = new(new Dictionary<string, string> { [Screen] = "Service Screen" },
        "https://identity.invalid/Identity/device");

    public async ValueTask InitializeAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        fixture.Device = Grant;
        await fixture.AllowDeviceSignInAsync();
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record Poll(HttpStatusCode Status, AuthOutcome Outcome)
    {
        public string? Error => (Outcome as AuthenticationRefused)?.Error;
        public AuthenticationFailure? Code => (Outcome as AuthenticationRefused)?.Code;
    }

    private static async Task<DeviceAuthorizationStarted> AuthorizeAsync(IdentityHttpHost host, string client = Screen) =>
        Assert.IsType<DeviceAuthorizationStarted>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync(
            "/api/identity/v2/device/authorize", new StartDeviceAuthorizationRequest(client))));

    private static async Task<Poll> PollAsync(IdentityHttpHost host, string deviceCode)
    {
        using var response = await host.Client.PostAsJsonAsync("/api/identity/v2/device/token", new DeviceTokenRequest(deviceCode));
        return new(response.StatusCode, (await response.Content.ReadFromJsonAsync<AuthOutcome>())!);
    }

    // The phone's requests carry no session: the approval's proof is the typed password.
    private async Task<AuthOutcome> ApproveAsync(IdentityHttpHost host, string userCode, string? username = null, string? password = null) =>
        await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/device/approve",
            new ApproveDeviceRequest(userCode, username ?? fixture.Username, password ?? fixture.Password)));

    private static async Task<AuthOutcome> DenyAsync(IdentityHttpHost host, string userCode) =>
        await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/device/deny", new DeviceUserCodeRequest(userCode)));

    private static async Task<AuthOutcome> LookupAsync(IdentityHttpHost host, string userCode) =>
        await IdentityHttpHost.Read(await host.Client.GetAsync("/api/identity/v2/device/" + Uri.EscapeDataString(userCode)));

    private async Task<AuthOutcome> LoginAsAsync(IdentityHttpHost host, string username) =>
        await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(username, fixture.Password, IdentityHttpHost.Pkce().Challenge)));

    private static AuthenticationFailure Code(AuthOutcome outcome) => Assert.IsType<AuthenticationRefused>(outcome).Code;

    private static string Claim(string token, string name) => new JsonWebToken(token).GetPayloadValue<string>(name);

    // The class shares one database, and usernames are unique in it.
    private static string NewUsername(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N")[..8];

    [Fact]
    public async Task An_approved_code_delivers_one_session_of_the_typed_account_that_renews_through_v2_refresh()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        Assert.Matches("^[BCDFGHJKLMNPQRSTVWXZ]{4}-[BCDFGHJKLMNPQRSTVWXZ]{4}$", started.UserCode);
        Assert.Equal("https://identity.invalid/Identity/device", started.VerificationUri);
        Assert.Equal("https://identity.invalid/Identity/device?code=" + started.UserCode, started.VerificationUriComplete);
        Assert.Equal((600, 5), (started.ExpiresIn, started.Interval));
        Assert.DoesNotContain(started.DeviceCode, started.VerificationUriComplete);
        var pending = await PollAsync(host, started.DeviceCode);
        Assert.Equal((HttpStatusCode.BadRequest, "authorization_pending", AuthenticationFailure.AuthorizationPending), (pending.Status, pending.Error, pending.Code));

        // Lowercase and without the dash: what a person may type. Before approval no account is named.
        var view = Assert.IsType<DeviceAuthorizationView>(await LookupAsync(host, started.UserCode.Replace("-", "").ToLowerInvariant()));
        Assert.Equal((started.UserCode, "Service Screen", (string?)null, DeviceAuthorizationState.Pending),
            (view.UserCode, view.ClientDisplayName, view.AccountName, view.State));
        var approved = Assert.IsType<DeviceAuthorizationView>(await ApproveAsync(host, started.UserCode));
        Assert.Equal((DeviceAuthorizationState.Approved, "Synthetic User"), (approved.State, approved.AccountName));

        var delivered = await PollAsync(host, started.DeviceCode);
        Assert.Equal(HttpStatusCode.OK, delivered.Status);
        var session = Assert.IsType<SessionIssued>(delivered.Outcome).Session;
        Assert.True(session.TokenLifeTimeInSeconds is > 0 and <= 900);
        // The device signs in as the typed account, on this host's own client, by password and without MFA.
        Assert.Equal(fixture.UserID.ToString(), Claim(session.Token, "shift_uid"));
        Assert.Equal(("test-client", "test-api", "false", "local", "false"),
            (Claim(session.Token, "shift_client"), Claim(session.Token, "shift_resource"), Claim(session.Token, "shift_external"),
             Claim(session.Token, "shift_route"), Claim(session.Token, "shift_mfa")));
        // The unchanged refresh route renews it.
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.RefreshToken));

        await using var db = fixture.CreateContext();
        var row = await db.Set<DeviceAuthorization>().AsNoTracking().SingleAsync();
        Assert.Equal(DeviceAuthorizationState.Consumed, row.State);
        Assert.Null(row.DeviceCodeDigest);
        Assert.Null(row.UserCodeDigest);
        Assert.Equal(fixture.UserID, row.UserID);
        var audit = await db.Set<AuthenticationAuditEvent>().Where(x => x.OperationID == row.ID).Select(x => x.Outcome).ToListAsync();
        Assert.Equal(["DeviceApproved", "DeviceSessionIssued"], audit.Order().ToArray());
        // The phone never signed in: no ordinary session was issued.
        Assert.DoesNotContain("SessionIssued", await db.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync());
    }

    [Fact]
    public async Task The_user_code_can_never_poll_and_unknown_codes_are_refused()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        Assert.IsType<DeviceAuthorizationView>(await ApproveAsync(host, started.UserCode));
        foreach (var candidate in new[] { started.UserCode, started.UserCode.Replace("-", ""), started.VerificationUriComplete,
            started.DeviceCode[..^1] + (started.DeviceCode[^1] == 'A' ? 'B' : 'A') })
        {
            var poll = await PollAsync(host, candidate);
            Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), (poll.Status, poll.Error));
        }
        // None of them touched the approved row: the device still collects its session.
        Assert.IsType<SessionIssued>((await PollAsync(host, started.DeviceCode)).Outcome);
    }

    [Fact]
    public async Task A_consumed_device_code_cannot_be_reused_and_its_user_code_no_longer_resolves()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        await ApproveAsync(host, started.UserCode);
        Assert.IsType<SessionIssued>((await PollAsync(host, started.DeviceCode)).Outcome);
        var again = await PollAsync(host, started.DeviceCode);
        Assert.Equal((HttpStatusCode.BadRequest, "invalid_grant"), (again.Status, again.Error));
        Assert.Equal(AuthenticationFailure.InvalidGrant, Code(await LookupAsync(host, started.UserCode)));
        Assert.Equal(AuthenticationFailure.InvalidGrant, Code(await ApproveAsync(host, started.UserCode)));
    }

    [Theory]
    [InlineData("deactivate", AuthenticationFailure.AccountUnavailable)]
    [InlineData("delete", AuthenticationFailure.AccountUnavailable)]
    [InlineData("version", AuthenticationFailure.StaleOperation)]
    [InlineData("factor", AuthenticationFailure.StaleOperation)]
    [InlineData("step", AuthenticationFailure.StaleOperation)]
    [InlineData("disallow", AuthenticationFailure.DeviceSignInNotAllowed)]
    public async Task An_account_change_after_approval_refuses_the_session_with_access_denied(string change, AuthenticationFailure expected)
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        await ApproveAsync(host, started.UserCode);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            switch (change)
            {
                case "deactivate": user.IsActive = false; break;
                case "delete": user.IsDeleted = true; break;
                case "version": state.SecurityVersion++; break;
                case "factor": state.FactorGeneration++; break;
                case "step": user.RequireChangePassword = true; break;
                // Without the version increment an administrator's save adds, to show the issue checks it on its own.
                case "disallow": user.AllowDeviceSignIn = false; break;
            }
            await db.SaveChangesAsync();
        }
        var poll = await PollAsync(host, started.DeviceCode);
        Assert.Equal((HttpStatusCode.BadRequest, "access_denied", expected), (poll.Status, poll.Error, poll.Code));
    }

    [Fact]
    public async Task Deactivating_or_bumping_the_version_after_issue_ends_the_device_session_at_its_next_refresh()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        await ApproveAsync(host, started.UserCode);
        var session = Assert.IsType<SessionIssued>((await PollAsync(host, started.DeviceCode)).Outcome).Session;
        await using (var db = fixture.CreateContext())
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, y => y.SecurityVersion + 1));
        Assert.Equal(AuthenticationFailure.StaleOperation, Code(await host.RefreshAsync(session.RefreshToken)));
    }

    [Fact]
    public async Task An_early_poll_gets_slow_down_and_the_interval_grows_by_five_seconds()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        Assert.Equal("authorization_pending", (await PollAsync(host, started.DeviceCode)).Error);
        clock.Advance(TimeSpan.FromSeconds(2));
        var early = await PollAsync(host, started.DeviceCode);
        Assert.Equal((HttpStatusCode.BadRequest, "slow_down", AuthenticationFailure.SlowDown), (early.Status, early.Error, early.Code));
        Assert.Equal(10, await ReadIntervalAsync());
        // The interval is now 10 seconds from the early poll: 5 seconds later is still early.
        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("slow_down", (await PollAsync(host, started.DeviceCode)).Error);
        Assert.Equal(15, await ReadIntervalAsync());
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal("authorization_pending", (await PollAsync(host, started.DeviceCode)).Error);
    }

    private async Task<int> ReadIntervalAsync()
    {
        await using var db = fixture.CreateContext();
        return (await db.Set<DeviceAuthorization>().AsNoTracking().SingleAsync()).Interval;
    }

    [Fact]
    public async Task An_expired_code_answers_expired_token_once_and_can_no_longer_be_approved()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        using var host = new IdentityHttpHost(fixture);
        var polled = await AuthorizeAsync(host);
        var unpolled = await AuthorizeAsync(host);
        clock.Advance(TimeSpan.FromSeconds(600));
        var poll = await PollAsync(host, polled.DeviceCode);
        Assert.Equal((HttpStatusCode.BadRequest, "expired_token", AuthenticationFailure.ExpiredToken), (poll.Status, poll.Error, poll.Code));
        // The codes are cleared: the device asks for a new pair.
        Assert.Equal("invalid_grant", (await PollAsync(host, polled.DeviceCode)).Error);
        // A code nobody polled expires when the phone reaches it.
        Assert.Equal(AuthenticationFailure.Expired, Code(await ApproveAsync(host, unpolled.UserCode)));
        Assert.Equal("invalid_grant", (await PollAsync(host, unpolled.DeviceCode)).Error);
        await using var db = fixture.CreateContext();
        Assert.All(await db.Set<DeviceAuthorization>().AsNoTracking().ToListAsync(), x => Assert.Equal(DeviceAuthorizationState.Expired, x.State));
    }

    [Fact]
    public async Task Anyone_with_the_code_can_deny_it_and_a_denied_code_cannot_be_approved_afterwards()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        var denied = Assert.IsType<DeviceAuthorizationView>(await DenyAsync(host, started.UserCode));
        Assert.Equal((DeviceAuthorizationState.Denied, (string?)null), (denied.State, denied.AccountName));
        var poll = await PollAsync(host, started.DeviceCode);
        Assert.Equal((HttpStatusCode.BadRequest, "access_denied", AuthenticationFailure.AccessDenied), (poll.Status, poll.Error, poll.Code));
        Assert.Equal(AuthenticationFailure.StaleOperation, Code(await ApproveAsync(host, started.UserCode)));
        Assert.Equal(DeviceAuthorizationState.Denied, Assert.IsType<DeviceAuthorizationView>(await LookupAsync(host, started.UserCode)).State);
        Assert.Equal("access_denied", (await PollAsync(host, started.DeviceCode)).Error);
        await using var db = fixture.CreateContext();
        Assert.Null((await db.Set<DeviceAuthorization>().AsNoTracking().SingleAsync()).UserID);
    }

    [Fact]
    public async Task A_poll_that_races_an_approval_waits_for_it_and_receives_the_session()
    {
        using var setup = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(setup);
        using var gate = new AdmissionGate("DeviceLocked");
        using var approver = new IdentityHttpHost(fixture, observe: gate.Observe);
        var signal = new SqlCommandSignal("[DeviceAuthorizations] WITH (UPDLOCK, HOLDLOCK)");
        using var screen = new IdentityHttpHost(fixture, null, null, signal);
        var approval = ApproveAsync(approver, started.UserCode);
        Task<Poll> poll;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            poll = PollAsync(screen, started.DeviceCode);
            // The poll reaches the row's lock and waits there while the approval holds it.
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(200);
            Assert.False(poll.IsCompleted);
        }
        finally { gate.Release(); }
        Assert.IsType<DeviceAuthorizationView>(await approval);
        Assert.IsType<SessionIssued>((await poll).Outcome);
    }

    [Fact]
    public async Task Concurrent_polls_of_an_approved_code_deliver_exactly_one_session()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        await ApproveAsync(host, started.UserCode);
        // A poll that arrives after the session was delivered meets an unknown code, which is counted per address.
        // The bucket exists before the race, so every such poll is counted in one window.
        Assert.Equal("invalid_grant", (await PollAsync(host, "unknown")).Error);
        var polls = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PollAsync(host, started.DeviceCode)));
        Assert.Single(polls, x => x.Outcome is SessionIssued);
        Assert.All(polls.Where(x => x.Outcome is not SessionIssued), x => Assert.Equal("invalid_grant", x.Error));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "DeviceSessionIssued"));
    }

    [Fact]
    public async Task Concurrent_approval_and_denial_leave_one_decision()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        // The address bucket exists before the race, so both requests are counted in one window.
        Assert.IsType<DeviceAuthorizationView>(await LookupAsync(host, started.UserCode));
        var results = await Task.WhenAll(ApproveAsync(host, started.UserCode), DenyAsync(host, started.UserCode));
        Assert.Single(results, x => x is DeviceAuthorizationView);
        Assert.Single(results, x => x is AuthenticationRefused { Code: AuthenticationFailure.StaleOperation });
    }

    [Fact]
    public async Task Only_the_right_password_of_an_account_that_allows_device_sign_in_and_owes_nothing_approves()
    {
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        // An unknown account and a wrong password look the same, as at login.
        Assert.Equal(AuthenticationFailure.InvalidProof, Code(await ApproveAsync(host, started.UserCode, username: NewUsername("nobody"))));
        Assert.Equal(AuthenticationFailure.InvalidProof, Code(await ApproveAsync(host, started.UserCode, password: "wrong")));
        // An account that does not allow device sign-in is told so only after its right password.
        var otherName = NewUsername("device-not-allowed");
        var other = await fixture.CreateSyntheticUserAsync(otherName);
        Assert.Equal(AuthenticationFailure.InvalidProof, Code(await ApproveAsync(host, started.UserCode, otherName, "wrong")));
        Assert.Equal(AuthenticationFailure.DeviceSignInNotAllowed, Code(await ApproveAsync(host, started.UserCode, otherName)));
        // An allowed account that owes a step must sign in normally first; the challenge cannot be continued here.
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(y => y.RequireChangePassword, true));
        var challenge = Assert.IsType<ChallengeRequired>(await ApproveAsync(host, started.UserCode)).Challenge;
        Assert.Equal(AuthenticationStep.PasswordChange, challenge.Step);
        Assert.Null(challenge.Handle);
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x
                .SetProperty(y => y.RequireChangePassword, false).SetProperty(y => y.IsActive, false));
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Code(await ApproveAsync(host, started.UserCode)));
        // None of it touched the code, and the audit names each refusal with the code's row.
        Assert.Equal("authorization_pending", (await PollAsync(host, started.DeviceCode)).Error);
        await using var check = fixture.CreateContext();
        var row = await check.Set<DeviceAuthorization>().AsNoTracking().SingleAsync();
        Assert.Equal(DeviceAuthorizationState.Pending, row.State);
        Assert.Null(row.UserID);
        var audits = await check.Set<AuthenticationAuditEvent>().Where(x => x.OperationID == row.ID).Select(x => new { x.UserID, x.Outcome }).ToListAsync();
        Assert.Equal(2, audits.Count(x => x.Outcome == "InvalidPassword"));
        Assert.Single(audits, x => x.Outcome == "DeviceSignInRefused" && x.UserID == other);
    }

    [Fact]
    public async Task Wrong_passwords_count_against_the_account_as_at_login()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        for (var i = 0; i < 10; i++)
            Assert.Equal(AuthenticationFailure.InvalidProof, Code(await ApproveAsync(host, started.UserCode, password: "wrong")));
        // The login's budget: now even the right password is refused, here and at login.
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await ApproveAsync(host, started.UserCode)));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)));
        // Once the window has passed, the right password signs a new code in and clears the count.
        clock.Advance(TimeSpan.FromMinutes(16));
        var next = await AuthorizeAsync(host);
        Assert.IsType<DeviceAuthorizationView>(await ApproveAsync(host, next.UserCode));
        await using var db = fixture.CreateContext();
        var state = await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID);
        Assert.Equal((0, (DateTimeOffset?)null), (state.FailedProofs, state.FailureWindowStart));
    }

    [Fact]
    public async Task An_account_that_allows_device_sign_in_is_never_asked_for_mfa_even_when_it_is_mandatory()
    {
        // The fixture user has an authenticator, and the host makes MFA mandatory for every account.
        await fixture.ResetAsync(mfa: true);
        await fixture.ChangeMfaPolicyAsync(mandatory: true);
        fixture.Device = Grant;
        await fixture.AllowDeviceSignInAsync();
        var plain = NewUsername("device-mfa-plain");
        await fixture.CreateSyntheticUserAsync(plain);
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        Assert.IsType<DeviceAuthorizationView>(await ApproveAsync(host, started.UserCode));
        var session = Assert.IsType<SessionIssued>((await PollAsync(host, started.DeviceCode)).Outcome).Session;
        Assert.Equal("false", Claim(session.Token, "shift_mfa"));
        // It renews: the account owes no MFA step.
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.RefreshToken));
        // The account's ordinary sign-in skips MFA too, while an account without device sign-in must set it up.
        Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        Assert.Equal(AuthenticationStep.NewMfa, Assert.IsType<ChallengeRequired>(await LoginAsAsync(host, plain)).Challenge.Step);
        // Taken away, the authenticator the account kept applies again.
        await fixture.AllowDeviceSignInAsync(false);
        Assert.Equal(AuthenticationStep.ExistingMfa,
            Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Challenge.Step);
    }

    [Fact]
    public async Task Unknown_clients_and_hosts_without_device_clients_are_refused()
    {
        using (var host = new IdentityHttpHost(fixture))
        {
            var refused = await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/device/authorize",
                new StartDeviceAuthorizationRequest("another-screen")));
            Assert.Equal(AuthenticationFailure.ClientDenied, Code(refused));
        }
        DeviceAuthorizationStarted started;
        using (var on = new IdentityHttpHost(fixture)) started = await AuthorizeAsync(on);
        fixture.Device = null;
        using var off = new IdentityHttpHost(fixture);
        Assert.Equal(AuthenticationFailure.ClientDenied, Code(await IdentityHttpHost.Read(
            await off.Client.PostAsJsonAsync("/api/identity/v2/device/authorize", new StartDeviceAuthorizationRequest(Screen)))));
        Assert.Equal("invalid_grant", (await PollAsync(off, started.DeviceCode)).Error);
        Assert.Equal(AuthenticationFailure.ClientDenied, Code(await LookupAsync(off, started.UserCode)));
        Assert.Equal(AuthenticationFailure.ClientDenied, Code(await ApproveAsync(off, started.UserCode)));
    }

    [Fact]
    public async Task Lookups_approvals_and_denials_cost_the_address()
    {
        fixture.DeliveryLimits = SqlIdentityFixture.TestDeliveryLimits with { DeviceAuthorizationsPerIpPer15Minutes = 3, DeviceLookupsPerIpPer15Minutes = 4 };
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        await AuthorizeAsync(host);
        await AuthorizeAsync(host);
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await IdentityHttpHost.Read(
            await host.Client.PostAsJsonAsync("/api/identity/v2/device/authorize", new StartDeviceAuthorizationRequest(Screen)))));
        // Four requests on the phone's routes, whatever they are, and then the address waits.
        Assert.IsType<DeviceAuthorizationView>(await LookupAsync(host, started.UserCode));
        Assert.Equal(AuthenticationFailure.InvalidProof, Code(await ApproveAsync(host, started.UserCode, password: "wrong")));
        Assert.IsType<DeviceAuthorizationView>(await LookupAsync(host, started.UserCode));
        Assert.IsType<DeviceAuthorizationView>(await LookupAsync(host, started.UserCode));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await LookupAsync(host, started.UserCode)));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await ApproveAsync(host, started.UserCode)));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await DenyAsync(host, started.UserCode)));
        // The device, which holds its own code, is not slowed by any of it.
        Assert.Equal("authorization_pending", (await PollAsync(host, started.DeviceCode)).Error);
    }

    [Fact]
    public async Task Codes_that_match_nothing_use_up_the_address_budget_for_them()
    {
        fixture.DeliveryLimits = SqlIdentityFixture.TestDeliveryLimits with { DeviceUnknownCodesPerIpPer15Minutes = 2, DeviceLookupFailuresPerIpPer15Minutes = 2 };
        using var host = new IdentityHttpHost(fixture);
        var started = await AuthorizeAsync(host);
        // Unknown device codes cost the address; a device that holds its own code is not slowed by them.
        Assert.Equal("invalid_grant", (await PollAsync(host, "unknown")).Error);
        Assert.Equal("invalid_grant", (await PollAsync(host, started.UserCode)).Error);
        Assert.Equal(("slow_down", AuthenticationFailure.AttemptsExhausted), ((await PollAsync(host, "unknown")).Error, (await PollAsync(host, "unknown")).Code));
        Assert.Equal("authorization_pending", (await PollAsync(host, started.DeviceCode)).Error);
        // Two user codes that match nothing use up the address's budget for them; then even the right code waits.
        Assert.Equal(AuthenticationFailure.InvalidGrant, Code(await LookupAsync(host, "BBBB-BBBB")));
        Assert.Equal(AuthenticationFailure.InvalidGrant, Code(await DenyAsync(host, "not a code")));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await LookupAsync(host, started.UserCode)));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Code(await ApproveAsync(host, started.UserCode)));
    }

    [Fact]
    public async Task Cleanup_expires_unfinished_codes_and_removes_old_rows()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        using var host = new IdentityHttpHost(fixture);
        var pending = await AuthorizeAsync(host);
        var denied = await AuthorizeAsync(host);
        await DenyAsync(host, denied.UserCode);
        clock.Advance(TimeSpan.FromMinutes(11));
        await using (var db = fixture.CreateContext())
            await new SqlIdentitySecurityStore(db).CleanupDeviceAuthorizationsAsync(clock.GetUtcNow());
        await using (var db = fixture.CreateContext())
        {
            var rows = await db.Set<DeviceAuthorization>().AsNoTracking().ToListAsync();
            Assert.Equal([DeviceAuthorizationState.Denied, DeviceAuthorizationState.Expired], rows.Select(x => x.State).Order().ToArray());
            Assert.All(rows, x => Assert.True(x.DeviceCodeDigest is null && x.UserCodeDigest is null));
        }
        Assert.Equal("invalid_grant", (await PollAsync(host, pending.DeviceCode)).Error);
        clock.Advance(TimeSpan.FromHours(25));
        await using (var db = fixture.CreateContext())
        {
            await new SqlIdentitySecurityStore(db).CleanupDeviceAuthorizationsAsync(clock.GetUtcNow());
            Assert.Empty(await db.Set<DeviceAuthorization>().ToListAsync());
        }
    }

    [Fact]
    public async Task A_host_configures_device_clients_and_the_device_signs_in_through_the_public_registration()
    {
        fixture.Device = null;
        await using (var db = fixture.CreateContext())
            await db.Apps.IgnoreQueryFilters().Where(x => x.AppId == ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId).ExecuteDeleteAsync();
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, c =>
        {
            c.FrontEndUrl = "https://identity.invalid/";
            c.Authority.DeviceClients = new() { [Screen] = "Service Screen" };
        });
        var started = Assert.IsType<DeviceAuthorizationStarted>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync(
            "api/identity/v2/device/authorize", new StartDeviceAuthorizationRequest(Screen))));
        // The phone page defaults to the front end's /Identity/device.
        Assert.Equal("https://identity.invalid/Identity/device", started.VerificationUri);
        Assert.IsType<DeviceAuthorizationView>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("api/identity/v2/device/approve",
            new ApproveDeviceRequest(started.UserCode, fixture.Username, fixture.Password))));
        using var token = await host.Client.PostAsJsonAsync("api/identity/v2/device/token", new DeviceTokenRequest(started.DeviceCode));
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        var session = Assert.IsType<SessionIssued>(await token.Content.ReadFromJsonAsync<AuthOutcome>()).Session;
        Assert.Equal(ConfiguredIdentityHttpHost<IdentityTestDbContext>.ClientId, Claim(session.Token, "shift_client"));
        using var renewed = await host.Client.PostAsJsonAsync("api/identity/v2/refresh", new RenewSessionRequest(session.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
    }
}
