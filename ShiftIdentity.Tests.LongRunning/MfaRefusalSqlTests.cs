using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

public sealed partial class MfaLifecycleSqlTests
{
    [Theory]
    [InlineData("permission")]
    [InlineData("users-write-only")]
    [InlineData("blank-reference")]
    [InlineData("stale-admin")]
    [InlineData("disabled-admin")]
    [InlineData("stale-authentication")]
    [InlineData("self-reset")]
    [InlineData("wrong-client")]
    public async Task Admin_recovery_enforces_dedicated_current_permission_and_independent_verification(string scenario)
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        await using (var db = fixture.CreateContext())
        {
            if (scenario is "permission" or "users-write-only")
                await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree,
                    scenario == "permission" ? "{}" : "{\"ShiftIdentityActions\":{\"Users\":[\"w\"]}}"));
            if (scenario == "stale-admin") await db.Set<UserSecurityState>().Where(x => x.UserID == adminID).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 2));
            if (scenario == "disabled-admin") await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        }
        if (scenario == "stale-authentication")
        {
            clock.Advance(TimeSpan.FromHours(20));
            admin = Assert.IsType<SessionIssued>(await host.RefreshAsync(admin.Session.RefreshToken));
        }
        using var other = new IdentityHttpHost(fixture, new("other-client", "other-api"));
        var response = await (scenario == "wrong-client" ? other : host).IssueRecoveryAsync(admin.Session.Token,
            scenario == "self-reset" ? adminID : fixture.UserID, scenario == "blank-reference" ? "   " : "Synthetic independent check");
        Assert.IsType<AuthenticationRefused>(response);
        var state = await State(); Assert.Equal(1, state.SecurityVersion); Assert.False(state.LocalMfaRecoveryRequired);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.MfaRecovery));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("factor")]
    [InlineData("policy")]
    [InlineData("expired-password")]
    [InlineData("cancelled")]
    [InlineData("wrong-verifier")]
    [InlineData("wrong-client")]
    [InlineData("wrong-purpose")]
    public async Task Pending_factor_does_not_activate_from_stale_or_misbound_authorization(string scenario)
    {
        using var host = new IdentityHttpHost(fixture);
        var ready = await PrepareFactor(host);
        await using (var db = fixture.CreateContext())
        {
            if (scenario == "version") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 2));
            if (scenario == "factor") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.FactorGeneration, 2));
            if (scenario == "policy") await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, 2));
        }
        if (scenario == "expired-password") clock.Advance(TimeSpan.FromMinutes(5));
        if (scenario == "cancelled") Assert.IsType<OperationCancelled>(await host.CancelAsync(ready.Challenge.Handle!, ready.Verifier));
        using var other = new IdentityHttpHost(fixture, new("other-client", "other-api"));
        var result = scenario == "wrong-purpose" ? await host.ChangePasswordAsync(ready.Challenge.Handle!, PasswordChangeSqlTests.NewPassword, ready.Verifier)
            : await (scenario == "wrong-client" ? other : host).ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge),
                scenario == "wrong-verifier" ? IdentityHttpHost.Pkce().Verifier : ready.Verifier);
        Assert.IsType<AuthenticationRefused>(result);
        var state = await State(); Assert.Null(state.ProtectedTotpSecret);
        Assert.Equal(scenario == "version" ? 2 : 1, state.SecurityVersion);
    }

    [Fact]
    public async Task Enrollment_cannot_extend_the_original_ten_minute_deadline_by_delaying_password_proof()
    {
        using var host = new IdentityHttpHost(fixture);
        var session = await Login(host); var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await host.StartMfaAsync(session.Session.Token, pkce.Challenge), AuthenticationStep.Password);
        clock.Advance(TimeSpan.FromMinutes(9));
        var setup = Challenge(await host.MfaPasswordAsync(start.Handle!, fixture.Password, pkce.Verifier), AuthenticationStep.NewMfa);
        Assert.Equal(start.ExpiresAt, setup.ExpiresAt);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(setup.Handle!, Code(setup), pkce.Verifier));
        Assert.Null((await State()).ProtectedTotpSecret);
        await AssertNoPendingMaterial();
    }

    [Fact]
    public async Task Recovery_child_inherits_the_original_deadline_and_consumed_code_cannot_restart_it()
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        var root = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic deadline check"));
        clock.Advance(TimeSpan.FromMinutes(14));
        var pkce = IdentityHttpHost.Pkce();
        var setup = Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, root.Code, pkce.Challenge), AuthenticationStep.NewMfa);
        Assert.Equal(root.ExpiresAt, setup.ExpiresAt);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(setup.Handle!, Code(setup), pkce.Verifier));
        Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, root.Code, pkce.Challenge));
        Assert.True((await State()).LocalMfaRecoveryRequired); Assert.Null((await State()).ProtectedTotpSecret);
        await AssertNoPendingMaterial();
    }

    [Fact]
    public async Task New_factor_failures_lock_the_operation_and_clear_its_secret()
    {
        using var host = new IdentityHttpHost(fixture);
        var ready = await PrepareFactor(host);
        for (var i = 0; i < 5; i++) Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(ready.Challenge.Handle!, "00000000", ready.Verifier));
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier));
        await using var db = fixture.CreateContext();
        var locked = await db.Set<AuthenticationOperation>().SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.MfaEnrollment);
        Assert.Equal(AuthenticationOperationState.Locked, locked.State); Assert.Equal(5, locked.FailedAttempts);
        Assert.Null(locked.ProtectedPendingTotpSecret); Assert.Equal(5, (await State()).FailedProofs);
    }

    [Fact]
    public async Task Recovery_reissue_does_not_reset_the_shared_local_failure_budget()
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host); var pkce = IdentityHttpHost.Pkce();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic check " + attempt));
            for (var i = 0; i < 5; i++) Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, "wrong", pkce.Challenge));
        }
        var last = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic final check"));
        var refused = Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, last.Code, pkce.Challenge));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, refused.Code);
        Assert.Equal(10, (await State()).FailedProofs); Assert.Equal(2, (await State()).SecurityVersion);
    }

    [Fact]
    public async Task Pending_ciphertext_cannot_be_copied_to_another_operation()
    {
        using var host = new IdentityHttpHost(fixture);
        var a = await PrepareFactor(host); var b = await PrepareFactor(host);
        Assert.True(OperationCredential.TryRead(a.Challenge.Handle, fixture.Options.OperationKey, out var aID, out _));
        Assert.True(OperationCredential.TryRead(b.Challenge.Handle, fixture.Options.OperationKey, out var bID, out _));
        await using (var db = fixture.CreateContext())
        {
            var source = await db.Set<AuthenticationOperation>().SingleAsync(x => x.ID == aID);
            var target = await db.Set<AuthenticationOperation>().SingleAsync(x => x.ID == bID);
            target.ProtectedPendingTotpSecret = source.ProtectedPendingTotpSecret;
            await db.SaveChangesAsync();
        }
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(b.Challenge.Handle!, Code(a.Challenge), b.Verifier));
        Assert.Null((await State()).ProtectedTotpSecret);
    }

    private sealed record PreparedFactor(AuthenticationChallenge Challenge, string Verifier, SessionIssued Session);
    [Fact]
    public async Task Cleanup_clears_expired_payloads_without_changing_the_active_factor_or_account_version()
    {
        using var host = new IdentityHttpHost(fixture);
        var ready = await PrepareKind(host, "replace");
        var before = await State();
        clock.Advance(TimeSpan.FromMinutes(5));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow()));
        var after = await State();
        Assert.Equal(before.ProtectedTotpSecret, after.ProtectedTotpSecret); Assert.Equal(before.SecurityVersion, after.SecurityVersion);
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier));
        await AssertNoPendingMaterial();
        clock.Advance(TimeSpan.FromHours(25));
        await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow());
        Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.MfaReplacement));
    }

    [Fact]
    public async Task Cancelled_recovery_keeps_recovery_required_and_cannot_reuse_the_root_code()
    {
        using var host = new IdentityHttpHost(fixture);
        var ready = await PrepareKind(host, "recover");
        Assert.IsType<OperationCancelled>(await host.CancelAsync(ready.Challenge.Handle!, ready.Verifier));
        Assert.True((await State()).LocalMfaRecoveryRequired); Assert.Null((await State()).ProtectedTotpSecret);
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier));
        Challenge(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge), AuthenticationStep.MfaRecovery);
        await AssertNoPendingMaterial();
    }

    private async Task<PreparedFactor> PrepareFactor(IdentityHttpHost host, bool replace = false)
    {
        var session = await Login(host, replace); var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await host.StartMfaAsync(session.Session.Token, pkce.Challenge, replace), replace ? AuthenticationStep.ExistingMfa : AuthenticationStep.Password);
        if (replace) clock.Advance(TimeSpan.FromSeconds(30));
        var setup = replace ? await host.ExistingFactorAsync(start.Handle!, ActiveCode(), pkce.Verifier)
            : await host.MfaPasswordAsync(start.Handle!, fixture.Password, pkce.Verifier);
        return new(Challenge(setup, AuthenticationStep.NewMfa), pkce.Verifier, session);
    }

    [Fact]
    public async Task Recovery_new_factor_inherits_attempts_spent_on_password_and_code_proof()
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host); var pkce = IdentityHttpHost.Pkce();
        var grant = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic attempt inheritance"));
        for (var i = 0; i < 4; i++)
            Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, "wrong", grant.Code, pkce.Challenge));
        var pending = Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, grant.Code, pkce.Challenge), AuthenticationStep.NewMfa);
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(pending.Handle!, "00000000", pkce.Verifier));
        Assert.Equal(AuthenticationFailure.AttemptsExhausted, Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(pending.Handle!, Code(pending), pkce.Verifier)).Code);
        Assert.True((await State()).LocalMfaRecoveryRequired); Assert.Null((await State()).ProtectedTotpSecret);
        await AssertNoPendingMaterial();
    }
}
