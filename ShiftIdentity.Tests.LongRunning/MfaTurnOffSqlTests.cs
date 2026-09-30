using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Turning MFA off where the host's policy makes it optional: an administrator with the recovery permission, a recent
/// sign-in and a note, or the user with a current code from the authenticator. The authenticator, a pending recovery
/// and its code go, every session of the account ends, and the account signs in with the password only.
/// </summary>
public sealed partial class MfaLifecycleSqlTests
{
    [Fact, Trait("Category", "Http")]
    public async Task An_administrator_turns_off_MFA_and_the_user_then_signs_in_with_the_password_only()
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var old = await Login(host, mfa: true);
        var admin = await AdminLogin(host);
        var changed = Assert.IsType<AdminAccountChanged>(await host.TurnOffAccountMfaAsync(admin.Session.Token, fixture.UserID, " Synthetic ticket 7 "));
        Assert.Equal((AdminAccountChange.Mfa, true, 2L), (changed.Change, changed.Applied, changed.SecurityVersion));
        var state = await State();
        Assert.Null(state.ProtectedTotpSecret); Assert.Equal(0, state.TotpProtectionVersion); Assert.Null(state.LastAcceptedTotpStep);
        Assert.False(state.LocalMfaRecoveryRequired); Assert.Null(state.MfaRecoveryOperationID);
        Assert.Equal(2, state.SecurityVersion); Assert.Equal(2, state.FactorGeneration);
        await using (var verify = fixture.CreateContext())
        {
            var audit = await verify.Set<AuthenticationAuditEvent>().SingleAsync(x => x.UserID == fixture.UserID && x.Outcome == "MfaTurnedOff");
            Assert.Equal((adminID, "Synthetic ticket 7", 2L), (audit.ActorUserID!.Value, audit.VerificationReference, audit.SecurityVersion));
        }

        // Every session of the account ends, and the password alone now opens one.
        Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(old.Session.RefreshToken));
        var session = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        Assert.False(Assert.IsType<AuthenticatorStatus>(await host.ReadAuthenticatorAsync(session.Session.Token)).Enrolled);

        // Nothing is left to turn off: the answer says so, without another version or audit row.
        var again = Assert.IsType<AdminAccountChanged>(await host.TurnOffAccountMfaAsync(admin.Session.Token, fixture.UserID, "Synthetic ticket 8"));
        Assert.False(again.Applied);
        Assert.Equal(2, (await State()).SecurityVersion);
        await using (var verify = fixture.CreateContext())
            Assert.Equal(1, await verify.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "MfaTurnedOff"));

        // The user can set up an authenticator again: first enrollment asks for the password, as for a new account.
        var pkce = IdentityHttpHost.Pkce();
        Challenge(await host.StartMfaAsync(session.Session.Token, pkce.Challenge), AuthenticationStep.Password);
    }

    [Theory, Trait("Category", "Http")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Turning_off_MFA_ends_a_pending_recovery_and_its_code(bool recoveryStarted)
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        var recovery = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic check 9"));
        var pkce = IdentityHttpHost.Pkce();
        var child = recoveryStarted
            ? Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, recovery.Code, pkce.Challenge), AuthenticationStep.NewMfa) : null;

        Assert.True(Assert.IsType<AdminAccountChanged>(await host.TurnOffAccountMfaAsync(admin.Session.Token, fixture.UserID, "Synthetic ticket 10")).Applied);
        var state = await State();
        Assert.False(state.LocalMfaRecoveryRequired); Assert.Null(state.MfaRecoveryOperationID); Assert.Null(state.ProtectedTotpSecret);
        Assert.Equal(3, state.SecurityVersion); Assert.Equal(3, state.FactorGeneration);

        // The code, and a recovery already past it, lead nowhere now.
        Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, recovery.Code, pkce.Challenge));
        if (child is not null) Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(child.Handle!, Code(child), pkce.Verifier));
        Assert.Null((await State()).ProtectedTotpSecret);
        await using (var db = fixture.CreateContext())
        {
            var family = await db.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.MfaRecovery).ToListAsync();
            Assert.Equal(recoveryStarted ? 2 : 1, family.Count);
            Assert.DoesNotContain(family, x => x.State is AuthenticationOperationState.AwaitingRecoveryProof or AuthenticationOperationState.AwaitingNewFactor);
            Assert.All(family, x => { Assert.Null(x.RecoveryCodeDigest); Assert.Null(x.ProtectedPendingTotpSecret); Assert.Null(x.OutstandingRecoveryUserID); });
        }
        Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, pkce.Challenge));
    }

    [Theory]
    [InlineData("self", AuthenticationFailure.ClientDenied)]
    [InlineData("users-write-only", AuthenticationFailure.ClientDenied)]
    [InlineData("stale-authentication", AuthenticationFailure.ReauthenticationRequired)]
    [InlineData("mandatory", AuthenticationFailure.ClientDenied)]
    [InlineData("deleted", AuthenticationFailure.AccountUnavailable)]
    [InlineData("blank-reference", AuthenticationFailure.InvalidRequest)]
    public async Task Refused_administrator_turn_offs_change_nothing(string scenario, AuthenticationFailure expected)
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        await using (var db = fixture.CreateContext())
        {
            // Users write without the recovery permission is not enough, as for a recovery code.
            if (scenario == "users-write-only")
                await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\",\"d\"]}}"));
            if (scenario == "mandatory")
                await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.MfaMandatory, true));
            if (scenario == "deleted")
                await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsDeleted, true));
        }
        if (scenario == "stale-authentication")
        {
            clock.Advance(TimeSpan.FromHours(20));
            admin = Assert.IsType<SessionIssued>(await host.RefreshAsync(admin.Session.RefreshToken));
        }
        var before = await State();
        var refused = Assert.IsType<AuthenticationRefused>(await host.TurnOffAccountMfaAsync(admin.Session.Token,
            scenario == "self" ? adminID : fixture.UserID, scenario == "blank-reference" ? "   " : "Synthetic ticket 11"));
        Assert.Equal(expected, refused.Code);
        var after = await State();
        Assert.Equal((before.SecurityVersion, before.FactorGeneration, before.LocalMfaRecoveryRequired),
            (after.SecurityVersion, after.FactorGeneration, after.LocalMfaRecoveryRequired));
        Assert.Equal(before.ProtectedTotpSecret, after.ProtectedTotpSecret);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "MfaTurnedOff"));
    }

    // Like the other administrator account changes, an inactive account can be corrected before it is activated
    // again. A built-in account is not excluded, as it is not from recovery: the same permission and note apply.
    [Theory]
    [InlineData("inactive")]
    [InlineData("built-in")]
    public async Task Turning_off_MFA_also_applies_to_an_inactive_or_built_in_account(string scenario)
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        try
        {
            await using (var db = fixture.CreateContext())
            {
                if (scenario == "inactive") await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
                else await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, true));
            }
            Assert.True(Assert.IsType<AdminAccountChanged>(await host.TurnOffAccountMfaAsync(admin.Session.Token, fixture.UserID, "Synthetic ticket 12")).Applied);
            Assert.Null((await State()).ProtectedTotpSecret);
        }
        finally
        {
            await using var restore = fixture.CreateContext();
            await restore.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, false));
        }
    }

    [Fact, Trait("Category", "Http")]
    public async Task A_user_turns_off_their_own_MFA_with_a_current_code_and_only_this_device_stays_signed_in()
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var device = await Login(host, mfa: true);
        clock.Advance(TimeSpan.FromSeconds(30));
        var other = await Login(host, mfa: true);

        // A wrong code, and the code a sign-in has already used, are refused and count against the account.
        Assert.Equal(AuthenticationFailure.InvalidProof, Assert.IsType<AuthenticationRefused>(await host.TurnOffMfaAsync(device.Session.Token, "000000")).Code);
        Assert.Equal(AuthenticationFailure.InvalidProof, Assert.IsType<AuthenticationRefused>(await host.TurnOffMfaAsync(device.Session.Token, ActiveCode())).Code);
        var refused = await State();
        Assert.Equal(2, refused.FailedProofs); Assert.NotNull(refused.ProtectedTotpSecret); Assert.Equal(1, refused.SecurityVersion);

        clock.Advance(TimeSpan.FromSeconds(30));
        var changed = Assert.IsType<MfaChanged>(await host.TurnOffMfaAsync(device.Session.Token, ActiveCode()));
        var fresh = Assert.IsType<SessionIssued>(changed.Continuation);
        var state = await State();
        Assert.Null(state.ProtectedTotpSecret); Assert.False(state.LocalMfaRecoveryRequired);
        Assert.Equal(2, state.SecurityVersion); Assert.Equal(2, state.FactorGeneration); Assert.Equal(0, state.FailedProofs);

        // This device continues with the fresh session; the other device and this device's old session do not.
        Assert.IsType<SessionIssued>(await host.RefreshAsync(fresh.Session.RefreshToken));
        Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(device.Session.RefreshToken));
        Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(other.Session.RefreshToken));
        Assert.False(Assert.IsType<AuthenticatorStatus>(await host.ReadAuthenticatorAsync(fresh.Session.Token)).Enrolled);
        Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        await using var verify = fixture.CreateContext();
        var audit = await verify.Set<AuthenticationAuditEvent>().SingleAsync(x => x.Outcome == "MfaTurnedOff");
        Assert.Null(audit.ActorUserID); Assert.Null(audit.VerificationReference);
        Assert.Equal(2, await verify.Set<AuthenticationAuditEvent>().CountAsync(x => x.UserID == fixture.UserID && x.Outcome == "InvalidMfa"));
    }

    [Theory]
    [InlineData("mandatory", AuthenticationFailure.ClientDenied)]
    [InlineData("no-factor", AuthenticationFailure.StaleOperation)]
    [InlineData("exhausted", AuthenticationFailure.AttemptsExhausted)]
    [InlineData("refresh-token", AuthenticationFailure.InvalidGrant)]
    public async Task A_user_cannot_turn_off_MFA_where_it_is_required_or_without_a_usable_proof(string scenario, AuthenticationFailure expected)
    {
        await fixture.ResetAsync(mfa: scenario != "no-factor");
        using var host = new IdentityHttpHost(fixture);
        var session = await Login(host, mfa: scenario != "no-factor");
        await using (var db = fixture.CreateContext())
        {
            if (scenario == "mandatory")
                await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.MfaMandatory, true));
            if (scenario == "exhausted")
                await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x
                    .SetProperty(s => s.FailedProofs, 10).SetProperty(s => s.FailureWindowStart, clock.GetUtcNow()));
        }
        clock.Advance(TimeSpan.FromSeconds(30));
        var before = await State();
        var refused = Assert.IsType<AuthenticationRefused>(await host.TurnOffMfaAsync(
            scenario == "refresh-token" ? session.Session.RefreshToken : session.Session.Token, ActiveCode()));
        Assert.Equal(expected, refused.Code);
        var after = await State();
        Assert.Equal((before.SecurityVersion, before.FactorGeneration, before.FailedProofs), (after.SecurityVersion, after.FactorGeneration, after.FailedProofs));
        Assert.Equal(before.ProtectedTotpSecret, after.ProtectedTotpSecret);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "MfaTurnedOff"));
    }

    // The account screens offer to turn MFA off only where the host's policy makes it optional.
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    public async Task The_authenticator_status_carries_whether_the_host_requires_MFA(bool enabled, bool mandatory, bool required)
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var session = await Login(host, mfa: true);
        await using (var db = fixture.CreateContext())
            await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.MfaEnabled, enabled).SetProperty(p => p.MfaMandatory, mandatory));
        var status = Assert.IsType<AuthenticatorStatus>(await host.ReadAuthenticatorAsync(session.Session.Token));
        Assert.Equal((true, false, required), (status.Enrolled, status.RecoveryRequired, status.Mandatory));
    }
}
