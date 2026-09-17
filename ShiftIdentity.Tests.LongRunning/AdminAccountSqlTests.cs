using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Administrator account mutations through the staged v2 routes: password set, username, email and active status.
/// Every applied change bumps the target's SecurityVersion, is attributed to the operator and never issues a session.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class AdminAccountSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "synthetic-account-admin";
    private const string OtherName = "synthetic-account-other";
    private const string OtherEmail = "other-account@example.invalid";
    private const string SavedEmail = "saved-account@example.invalid";
    private const string NewEmail = "New-Account@example.invalid";
    private const string UsersWrite = "{\"ShiftIdentityActions\":{\"Users\":[\"w\"]}}";
    private const string NewPassword = "Administrator chosen phrase 61!";
    private ControlledClock clock = null!;
    private long adminID, otherID;

    public async ValueTask InitializeAsync()
    {
        clock = new(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        adminID = await db.Users.IgnoreQueryFilters().Where(x => x.Username == AdminName).Select(x => x.ID).SingleOrDefaultAsync();
        if (adminID == 0) adminID = await fixture.CreateSyntheticUserAsync(AdminName, UsersWrite);
        otherID = await db.Users.IgnoreQueryFilters().Where(x => x.Username == OtherName).Select(x => x.ID).SingleOrDefaultAsync();
        if (otherID == 0) otherID = await fixture.CreateSyntheticUserAsync(OtherName, email: OtherEmail);
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == adminID || x.ID == otherID)
            .ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, true).SetProperty(u => u.IsDeleted, false));
        await db.Set<UserSecurityState>().Where(x => x.UserID == adminID || x.UserID == otherID).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.SecurityVersion, 1).SetProperty(s => s.FailedProofs, 0).SetProperty(s => s.FailureWindowStart, (DateTimeOffset?)null));
    }

    public ValueTask DisposeAsync() { fixture.Clock = TimeProvider.System; return ValueTask.CompletedTask; }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Password_set_bumps_version_ends_sessions_and_forces_change_only_when_requested(bool requireChange)
    {
        using var host = new IdentityHttpHost(fixture);
        var target = await Login(host, fixture.Username);
        var admin = await AdminLogin(host);
        var changed = Assert.IsType<AdminAccountChanged>(await host.AdminSetPasswordAsync(admin.Session.Token, fixture.UserID, NewPassword, requireChange));
        Assert.Equal(AdminAccountChange.Password, changed.Change); Assert.True(changed.Applied); Assert.Equal(2, changed.SecurityVersion); Assert.Null(changed.Delivery);
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(target.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.InvalidProof, Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Code);
        var login = await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, NewPassword);
        if (requireChange) Assert.Equal(AuthenticationStep.PasswordChange, Assert.IsType<ChallengeRequired>(login).Challenge.Step);
        else Assert.IsType<SessionIssued>(login);
        // The operator's own session and version are untouched.
        Assert.IsType<SessionIssued>(await host.RefreshAsync(admin.Session.RefreshToken));
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.Equal(requireChange, user.RequireChangePassword);
        Assert.True(HashService.VerifyVersionedPassword(NewPassword, user.Salt, user.PasswordHash));
        var audit = await db.Set<AuthenticationAuditEvent>().SingleAsync(x => x.UserID == fixture.UserID && x.Outcome.StartsWith("AdminPasswordSet"));
        Assert.Equal(adminID, audit.ActorUserID); Assert.Equal(2, audit.SecurityVersion);
        Assert.Equal(requireChange ? "AdminPasswordSetRequiringChange" : "AdminPasswordSet", audit.Outcome);
        Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == adminID)).SecurityVersion);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("same")]
    [InlineData("empty")]
    public async Task Password_set_refuses_policy_failures_without_changing_anything(string kind)
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        var password = kind switch { "same" => fixture.Password, "empty" => "", _ => "short" };
        var refused = Assert.IsType<AuthenticationRefused>(await host.AdminSetPasswordAsync(admin.Session.Token, fixture.UserID, password));
        if (kind == "empty") Assert.Equal(AuthenticationFailure.InvalidRequest, refused.Code);
        else
        {
            Assert.Equal(AuthenticationFailure.InvalidNewPassword, refused.Code);
            Assert.NotNull(refused.PasswordFailure);
            Assert.Equal(kind == "same", refused.PasswordFailure == PasswordPolicyFailure.SameAsCurrent);
        }
        await AssertUnchanged();
    }

    [Theory]
    [InlineData("password", "permission", AuthenticationFailure.ClientDenied)]
    [InlineData("password", "self", AuthenticationFailure.ClientDenied)]
    [InlineData("password", "stale", AuthenticationFailure.InvalidProof)]
    [InlineData("password", "protected", AuthenticationFailure.ClientDenied)]
    [InlineData("password", "deleted", AuthenticationFailure.AccountUnavailable)]
    [InlineData("password", "noSession", AuthenticationFailure.InvalidGrant)]
    [InlineData("username", "permission", AuthenticationFailure.ClientDenied)]
    [InlineData("username", "self", AuthenticationFailure.ClientDenied)]
    [InlineData("username", "stale", AuthenticationFailure.InvalidProof)]
    [InlineData("username", "protected", AuthenticationFailure.ClientDenied)]
    [InlineData("username", "deleted", AuthenticationFailure.AccountUnavailable)]
    [InlineData("username", "noSession", AuthenticationFailure.InvalidGrant)]
    [InlineData("email", "permission", AuthenticationFailure.ClientDenied)]
    [InlineData("email", "self", AuthenticationFailure.ClientDenied)]
    [InlineData("email", "stale", AuthenticationFailure.InvalidProof)]
    [InlineData("email", "protected", AuthenticationFailure.ClientDenied)]
    [InlineData("email", "deleted", AuthenticationFailure.AccountUnavailable)]
    [InlineData("email", "noSession", AuthenticationFailure.InvalidGrant)]
    [InlineData("status", "permission", AuthenticationFailure.ClientDenied)]
    [InlineData("status", "self", AuthenticationFailure.ClientDenied)]
    [InlineData("status", "stale", AuthenticationFailure.InvalidProof)]
    [InlineData("status", "protected", AuthenticationFailure.ClientDenied)]
    [InlineData("status", "deleted", AuthenticationFailure.AccountUnavailable)]
    [InlineData("status", "noSession", AuthenticationFailure.InvalidGrant)]
    public async Task Admin_mutations_require_users_write_fresh_proof_and_an_eligible_target(string route, string scenario, AuthenticationFailure expected)
    {
        using var host = new IdentityHttpHost(fixture);
        var actor = scenario == "permission" ? await Login(host, OtherName) : await AdminLogin(host);
        var access = scenario == "noSession" ? "not-a-session" : actor.Session.Token;
        var target = scenario == "self" ? adminID : fixture.UserID;
        try
        {
            if (scenario == "stale") clock.Advance(TimeSpan.FromMinutes(6));
            if (scenario is "protected" or "deleted" or "inactive") await SetTarget(scenario);
            var result = Assert.IsType<AuthenticationRefused>(await Mutate(host, route, access, target));
            Assert.Equal(expected, result.Code);
            await SetTarget("restore");
            await AssertUnchanged();
            await using var db = fixture.CreateContext();
            Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == adminID)).SecurityVersion);
        }
        finally { await SetTarget("restore"); }
    }

    [Fact]
    public async Task Username_change_updates_login_and_lookup_and_invalidates_sessions()
    {
        using var host = new IdentityHttpHost(fixture);
        var target = await Login(host, fixture.Username);
        var admin = await AdminLogin(host);
        var renamed = "Renamed-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var changed = Assert.IsType<AdminAccountChanged>(await host.AdminChangeUsernameAsync(admin.Session.Token, fixture.UserID, "  " + renamed + "  "));
            Assert.Equal(AdminAccountChange.Username, changed.Change); Assert.True(changed.Applied); Assert.Equal(2, changed.SecurityVersion);
            Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(target.Session.RefreshToken)).Code);
            Assert.Equal(AuthenticationFailure.InvalidProof, Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Code);
            Assert.IsType<SessionIssued>(await Login(host, renamed));
            await using (var db = fixture.CreateContext())
            {
                var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
                var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
                Assert.Equal(renamed, user.Username); Assert.Equal(renamed.ToUpperInvariant(), state.UsernameLookupKey);
                Assert.True(RecoveryContact.LookupMatches(user, state));
                var audit = await db.Set<AuthenticationAuditEvent>().SingleAsync(x => x.UserID == fixture.UserID && x.Outcome == "AdminUsernameChanged");
                Assert.Equal(adminID, audit.ActorUserID);
            }
            var unchanged = Assert.IsType<AdminAccountChanged>(await host.AdminChangeUsernameAsync(admin.Session.Token, fixture.UserID, renamed));
            Assert.False(unchanged.Applied); Assert.Equal(2, unchanged.SecurityVersion);
            var restored = Assert.IsType<AdminAccountChanged>(await host.AdminChangeUsernameAsync(admin.Session.Token, fixture.UserID, fixture.Username));
            Assert.True(restored.Applied); Assert.Equal(3, restored.SecurityVersion);
            Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        }
        finally { await RestoreUsername(); }
    }

    [Theory]
    [InlineData("SYNTHETIC-ACCOUNT-OTHER", AuthenticationFailure.DuplicateIdentifier, HttpStatusCode.BadRequest)]
    [InlineData(" Other-Account@Example.Invalid ", AuthenticationFailure.DuplicateIdentifier, HttpStatusCode.BadRequest)]
    [InlineData("synthetic-account-admin", AuthenticationFailure.DuplicateIdentifier, HttpStatusCode.BadRequest)]
    [InlineData("   ", AuthenticationFailure.InvalidRequest, HttpStatusCode.BadRequest)]
    [InlineData("badname", AuthenticationFailure.InvalidRequest, HttpStatusCode.BadRequest)]
    public async Task Username_change_refuses_identifiers_used_by_other_accounts_and_invalid_names(string requested, AuthenticationFailure expected, HttpStatusCode status)
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/admin/username") { Content = JsonContent.Create(new AdminUsernameChangeRequest(fixture.UserID, requested)) };
        request.Headers.Authorization = new("Bearer", admin.Session.Token);
        var response = await host.Client.SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(expected, Assert.IsType<AuthenticationRefused>(await IdentityHttpHost.Read(response)).Code);
        await AssertUnchanged();
    }

    [Theory]
    [InlineData("send")]
    [InlineData("silent")]
    [InlineData("failing")]
    public async Task Email_change_clears_verification_records_admin_authority_and_delivers_verification_separately(string delivery)
    {
        await Contact(SavedEmail, verified: true);
        using var host = new IdentityHttpHost(fixture);
        var target = await Login(host, fixture.Username);
        var admin = await AdminLogin(host);
        Inbox.FailDeliveries = delivery == "failing";
        var changed = Assert.IsType<AdminAccountChanged>(await host.AdminChangeEmailAsync(admin.Session.Token, fixture.UserID, " " + NewEmail + " ", delivery != "silent"));
        Assert.Equal(AdminAccountChange.Email, changed.Change); Assert.True(changed.Applied); Assert.Equal(2, changed.SecurityVersion);
        switch (delivery)
        {
            case "send": Assert.IsType<SecurityDeliveryRequested>(changed.Delivery); break;
            case "silent": Assert.Null(changed.Delivery); break;
            default: Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(changed.Delivery).Code); break;
        }
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(target.Session.RefreshToken)).Code);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            Assert.Equal(NewEmail, user.Email); Assert.False(user.EmailVerified); Assert.Null(user.VerificationSASToken);
            Assert.Equal(2, state.SecurityVersion); Assert.Equal(2, state.ContactRevision); Assert.Equal(NewEmail.ToUpperInvariant(), state.EmailLookupKey);
            Assert.Equal(RecoveryEmailProvenance.TrustedAdminAssignment, state.RecoveryEmailProvenance);
            Assert.True(RecoveryContact.IsEligible(user, state));
            var audit = await db.Set<AuthenticationAuditEvent>().SingleAsync(x => x.UserID == fixture.UserID && x.Outcome == "AdminEmailChanged");
            Assert.Equal(adminID, audit.ActorUserID);
            var links = await db.Set<AuthenticationOperation>().Where(x => x.UserID == fixture.UserID && x.Purpose == AuthenticationOperationPurpose.EmailVerify).ToListAsync();
            if (delivery == "silent") Assert.Empty(links);
            else
            {
                var link = Assert.Single(links);
                Assert.Equal(2, link.SecurityVersion); Assert.Equal(2, link.ContactRevision);
                Assert.Equal(delivery == "send" ? AuthenticationOperationState.AwaitingExplicitSubmit : AuthenticationOperationState.Cancelled, link.State);
                if (delivery == "send") Assert.Equal(NewEmail, link.Destination);
            }
        }
        if (delivery != "send") { Assert.Empty(Inbox.Messages); return; }
        var message = Assert.Single(Inbox.Messages);
        Assert.Equal(NewEmail, message.Destination); Assert.Equal("Verify your email", message.Subject);
        var grant = Uri.UnescapeDataString(message.Link.Split("#grant=", 2)[1].Split('&', 2)[0]);
        var page = Assert.IsType<SecurityLinkOpened>(await Post(host, "security-link/open", new OpenSecurityLinkRequest(grant, AuthenticationOperationPurpose.EmailVerify)));
        Assert.IsType<EmailVerificationCompleted>(await Post(host, "email-verification/complete", new CompleteEmailVerificationRequest(page.PageHandle)));
        await using var verified = fixture.CreateContext();
        var after = await verified.Users.SingleAsync(x => x.ID == fixture.UserID);
        var afterState = await verified.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        Assert.True(after.EmailVerified); Assert.Equal(RecoveryEmailProvenance.OwnershipVerification, afterState.RecoveryEmailProvenance);
        Assert.Equal(2, afterState.SecurityVersion);
    }

    [Fact]
    public async Task Email_removal_clears_recovery_authority_without_delivery()
    {
        await Contact(SavedEmail, verified: true);
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        var changed = Assert.IsType<AdminAccountChanged>(await host.AdminChangeEmailAsync(admin.Session.Token, fixture.UserID, "   ", true));
        Assert.True(changed.Applied); Assert.Equal(2, changed.SecurityVersion); Assert.Null(changed.Delivery);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            Assert.Null(user.Email); Assert.False(user.EmailVerified);
            Assert.Null(state.EmailLookupKey); Assert.Null(state.RecoveryEmail); Assert.Equal(RecoveryEmailProvenance.Unknown, state.RecoveryEmailProvenance);
            Assert.Equal(2, state.ContactRevision); Assert.False(RecoveryContact.IsEligible(user, state));
        }
        // The old address no longer resolves to the account for public requests.
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "password-reset/request", new RequestSecurityEmail(SavedEmail)));
        Assert.Empty(Inbox.Messages);
    }

    [Theory]
    [InlineData("OTHER-ACCOUNT@EXAMPLE.INVALID", AuthenticationFailure.DuplicateIdentifier)]
    [InlineData("not an address", AuthenticationFailure.InvalidRequest)]
    [InlineData("Display Name <person@example.invalid>", AuthenticationFailure.InvalidRequest)]
    [InlineData("person@example.invalid", AuthenticationFailure.InvalidRequest)]
    public async Task Email_change_refuses_duplicates_and_invalid_addresses(string requested, AuthenticationFailure expected)
    {
        await Contact(SavedEmail, verified: true);
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        Assert.Equal(expected, Assert.IsType<AuthenticationRefused>(await host.AdminChangeEmailAsync(admin.Session.Token, fixture.UserID, requested)).Code);
        await AssertUnchanged(email: SavedEmail);
        Assert.Empty(Inbox.Messages);
    }

    [Fact]
    public async Task Equivalent_email_spelling_is_not_a_change()
    {
        await Contact(SavedEmail, verified: true);
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        var changed = Assert.IsType<AdminAccountChanged>(await host.AdminChangeEmailAsync(admin.Session.Token, fixture.UserID, "  " + SavedEmail.ToUpperInvariant() + "  ", true));
        Assert.False(changed.Applied); Assert.Equal(1, changed.SecurityVersion); Assert.Null(changed.Delivery);
        await AssertUnchanged(email: SavedEmail);
        Assert.Empty(Inbox.Messages);
    }

    [Fact]
    public async Task Deactivation_and_reactivation_each_bump_version_and_end_old_sessions()
    {
        using var host = new IdentityHttpHost(fixture);
        var target = await Login(host, fixture.Username);
        var admin = await AdminLogin(host);
        var off = Assert.IsType<AdminAccountChanged>(await host.AdminSetActiveAsync(admin.Session.Token, fixture.UserID, false));
        Assert.Equal(AdminAccountChange.Active, off.Change); Assert.True(off.Applied); Assert.Equal(2, off.SecurityVersion);
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(target.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Code);
        // An inactive account still accepts a correction; it holds no session and the change is versioned.
        var set = Assert.IsType<AdminAccountChanged>(await host.AdminSetPasswordAsync(admin.Session.Token, fixture.UserID, NewPassword, false));
        Assert.True(set.Applied); Assert.Equal(3, set.SecurityVersion);
        Assert.Equal(AuthenticationFailure.AccountUnavailable, Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, NewPassword)).Code);
        var same = Assert.IsType<AdminAccountChanged>(await host.AdminSetActiveAsync(admin.Session.Token, fixture.UserID, false));
        Assert.False(same.Applied); Assert.Equal(3, same.SecurityVersion);
        clock.Advance(TimeSpan.FromSeconds(1));
        var on = Assert.IsType<AdminAccountChanged>(await host.AdminSetActiveAsync(admin.Session.Token, fixture.UserID, true));
        Assert.True(on.Applied); Assert.Equal(4, on.SecurityVersion);
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(target.Session.RefreshToken)).Code);
        Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, NewPassword));
        await using var db = fixture.CreateContext();
        var audits = await db.Set<AuthenticationAuditEvent>().Where(x => x.UserID == fixture.UserID && x.ActorUserID == adminID)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.SecurityVersion).Select(x => x.Outcome).ToListAsync();
        Assert.Equal(new[] { "AccountDeactivated", "AdminPasswordSet", "AccountActivated" }, audits);
    }

    [Theory]
    [InlineData("username")]
    [InlineData("email")]
    public async Task Inactive_targets_accept_identifier_and_contact_corrections(string route)
    {
        await Contact(SavedEmail, verified: true);
        await SetTarget("inactive");
        try
        {
            using var host = new IdentityHttpHost(fixture);
            var admin = await AdminLogin(host);
            var changed = Assert.IsType<AdminAccountChanged>(await Mutate(host, route, admin.Session.Token, fixture.UserID));
            Assert.True(changed.Applied); Assert.Equal(2, changed.SecurityVersion);
            await using var db = fixture.CreateContext();
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.ID == fixture.UserID);
            Assert.False(user.IsActive);
            if (route == "username") Assert.NotEqual(fixture.Username, user.Username);
            else { Assert.NotEqual(SavedEmail, user.Email); Assert.False(user.EmailVerified); }
        }
        finally { await SetTarget("restore"); await RestoreUsername(); }
    }

    [Fact]
    public async Task Stale_admin_password_snapshot_cannot_overwrite_a_concurrent_credential_change()
    {
        using var setup = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(setup);
        using var gate = new AdmissionGate("AdminPasswordPrepared");
        using var gated = new IdentityHttpHost(fixture, observe: gate.Observe);
        var pending = gated.AdminSetPasswordAsync(admin.Session.Token, fixture.UserID, NewPassword);
        const string userChosen = "User chosen phrase 29!";
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var session = await Login(setup, fixture.Username);
            var pkce = IdentityHttpHost.Pkce();
            var start = Assert.IsType<ChallengeRequired>(await setup.StartPasswordChangeAsync(session.Session.Token, pkce.Challenge));
            var ready = Assert.IsType<ChallengeRequired>(await setup.ProvePasswordAsync(start.Challenge.Handle!, fixture.Password, pkce.Verifier));
            Assert.IsType<PasswordChanged>(await setup.ChangePasswordAsync(ready.Challenge.Handle!, userChosen, pkce.Verifier));
        }
        finally { gate.Release(); }
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await pending).Code);
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.True(HashService.VerifyVersionedPassword(userChosen, user.Salt, user.PasswordHash));
        Assert.False(user.RequireChangePassword);
        Assert.Equal(2, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).SecurityVersion);
        Assert.Empty(await db.Set<AuthenticationAuditEvent>().Where(x => x.UserID == fixture.UserID && x.ActorUserID != null).ToListAsync());
    }

    private async Task<SessionIssued> Login(IdentityHttpHost host, string username) =>
        Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(username, fixture.Password, IdentityHttpHost.Pkce().Challenge))));
    private Task<SessionIssued> AdminLogin(IdentityHttpHost host) => Login(host, AdminName);

    private static Task<AuthOutcome> Mutate(IdentityHttpHost host, string route, string access, long target) => route switch
    {
        "password" => host.AdminSetPasswordAsync(access, target, NewPassword),
        "username" => host.AdminChangeUsernameAsync(access, target, "renamed-" + Guid.NewGuid().ToString("N")[..8]),
        "email" => host.AdminChangeEmailAsync(access, target, "renamed-" + Guid.NewGuid().ToString("N")[..8] + "@example.invalid", false),
        _ => host.AdminSetActiveAsync(access, target, false)
    };

    private static async Task<AuthOutcome> Post<T>(IdentityHttpHost host, string path, T payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/" + path) { Content = JsonContent.Create(payload) };
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }

    private LocalSecurityInbox Inbox => Assert.IsType<LocalSecurityInbox>(fixture.EmailSink);

    private async Task SetTarget(string scenario)
    {
        await using var db = fixture.CreateContext();
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.IsProtected, scenario == "protected")
            .SetProperty(u => u.IsDeleted, scenario == "deleted")
            .SetProperty(u => u.IsActive, scenario != "inactive"));
    }

    private async Task Contact(string? email, bool verified)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        user.Email = email; user.EmailVerified = verified; RecoveryContact.Invalidate(state);
        state.UsernameLookupKey = null; state.EmailLookupKey = null; RecoveryContact.InitializeLookup(user, state);
        if (email is not null)
            RecoveryContact.RecordOwnership(user, state, verified ? RecoveryEmailProvenance.OwnershipVerification : RecoveryEmailProvenance.TrustedAdminAssignment);
        await db.SaveChangesAsync();
    }

    private async Task RestoreUsername()
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        user.Username = fixture.Username; state.UsernameLookupKey = RecoveryContact.Key(fixture.Username);
        await db.SaveChangesAsync();
    }

    private async Task AssertUnchanged(string? email = null)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.ID == fixture.UserID);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        Assert.Equal(fixture.Username, user.Username); Assert.Equal(email, user.Email); Assert.Equal(email is not null, user.EmailVerified);
        Assert.True(user.IsActive); Assert.False(user.IsDeleted); Assert.False(user.IsProtected); Assert.False(user.RequireChangePassword);
        Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
        Assert.Equal(1, state.SecurityVersion); Assert.Equal(1, state.ContactRevision);
        Assert.Empty(await db.Set<AuthenticationAuditEvent>().Where(x => x.UserID == fixture.UserID && x.ActorUserID != null).ToListAsync());
    }
}
