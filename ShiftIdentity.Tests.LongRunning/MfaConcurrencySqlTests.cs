using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

public sealed partial class MfaLifecycleSqlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Factor_activation_and_refresh_serialize_in_both_lock_orders(bool activationFirst)
    {
        using var setup = new IdentityHttpHost(fixture);
        var ready = await PrepareFactor(setup);
        using var gate = new AdmissionGate(activationFirst ? "MfaMutation" : "AdmissionLock");
        using var first = new IdentityHttpHost(fixture, observe: gate.Observe);
        var signal = new SqlCommandSignal("UPDLOCK");
        using var second = new IdentityHttpHost(fixture, null, null, signal);
        var a = activationFirst ? first.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier)
            : first.RefreshAsync(ready.Session.Session.RefreshToken);
        Task<AuthOutcome>? b = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            b = activationFirst ? second.RefreshAsync(ready.Session.Session.RefreshToken)
                : second.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(b.IsCompleted);
        }
        finally { gate.Release(); }
        var firstResult = await a; var secondResult = await b!;
        Assert.IsType<MfaChanged>(activationFirst ? firstResult : secondResult);
        if (activationFirst) Assert.IsType<AuthenticationRefused>(secondResult);
        else
        {
            var old = Assert.IsType<SessionIssued>(firstResult);
            Assert.True(old.Session.TokenLifeTimeInSeconds <= 900);
            Assert.IsType<AuthenticationRefused>(await setup.RefreshAsync(old.Session.RefreshToken));
        }
        await AssertActivated(ready.Challenge, 2, 2);
    }

    [Fact]
    public async Task Concurrent_confirmation_of_one_factor_issues_once()
    {
        using var setup = new IdentityHttpHost(fixture);
        var ready = await PrepareFactor(setup);
        using var gate = new AdmissionGate("MfaMutation");
        using var first = new IdentityHttpHost(fixture, observe: gate.Observe);
        var signal = new SqlCommandSignal("UPDLOCK"); using var second = new IdentityHttpHost(fixture, null, null, signal);
        var a = first.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier);
        Task<AuthOutcome>? b = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            b = second.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(b.IsCompleted);
        }
        finally { gate.Release(); }
        Assert.IsType<MfaChanged>(await a); Assert.IsType<AuthenticationRefused>(await b!);
        await AssertActivated(ready.Challenge, 2, 2);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.UserID == fixture.UserID && x.Outcome == "MfaEnrolled"));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("factor")]
    [InlineData("policy")]
    [InlineData("hash")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task Password_proof_cannot_be_rebased_after_its_snapshot_changes(string change)
    {
        using var setup = new IdentityHttpHost(fixture);
        var session = await Login(setup); var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await setup.StartMfaAsync(session.Session.Token, pkce.Challenge), AuthenticationStep.Password);
        using var gate = new AdmissionGate("MfaPasswordProof");
        using var host = new IdentityHttpHost(fixture, observe: gate.Observe);
        var proving = host.MfaPasswordAsync(start.Handle!, fixture.Password, pkce.Verifier);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = fixture.CreateContext();
            if (change == "version") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 2));
            if (change == "factor") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(s => s.FactorGeneration, 2));
            if (change == "policy") await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, 2));
            if (change == "hash")
            {
                var hash = HashService.GenerateVersionedHash(PasswordChangeSqlTests.NewPassword);
                await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.PasswordHash, hash.PasswordHash).SetProperty(u => u.Salt, hash.Salt));
            }
            if (change == "expiry") clock.Advance(TimeSpan.FromMinutes(10));
            if (change == "cancel") Assert.IsType<OperationCancelled>(await setup.CancelAsync(start.Handle!, pkce.Verifier));
        }
        finally { gate.Release(); }
        Assert.IsType<AuthenticationRefused>(await proving);
        Assert.Null((await State()).ProtectedTotpSecret);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync(x => x.ProtectedPendingTotpSecret != null));
    }

    [Fact]
    public async Task Recovery_proof_racing_reissue_cannot_authorize_the_new_root()
    {
        using var setup = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(setup);
        var first = Assert.IsType<MfaRecoveryCodeIssued>(await setup.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic first check"));
        var pkce = IdentityHttpHost.Pkce();
        using var gate = new AdmissionGate("RecoveryProof");
        using var host = new IdentityHttpHost(fixture, observe: gate.Observe);
        var proving = host.RecoverMfaAsync(fixture.Username, fixture.Password, first.Code, pkce.Challenge);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.IsType<MfaRecoveryCodeIssued>(await setup.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic reissue check"));
        }
        finally { gate.Release(); }
        Assert.IsType<AuthenticationRefused>(await proving);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync(x => x.ProtectedPendingTotpSecret != null));
        Assert.Equal(2, (await State()).SecurityVersion);
    }

    [Theory]
    [InlineData("enroll", "before-save")]
    [InlineData("enroll", "after-save")]
    [InlineData("enroll", "lost-response")]
    [InlineData("replace", "before-save")]
    [InlineData("replace", "after-save")]
    [InlineData("replace", "lost-response")]
    [InlineData("recover", "before-save")]
    [InlineData("recover", "after-save")]
    [InlineData("recover", "lost-response")]
    public async Task Activation_faults_never_leak_a_session_or_partially_change_factor_state(string kind, string fault)
    {
        using var setup = new IdentityHttpHost(fixture);
        var ready = await PrepareKind(setup, kind);
        var before = await State();
        IInterceptor interceptor = fault == "lost-response" ? new LostCommitResponse() : new CompletionSaveFault(fault == "after-save");
        using var failing = new IdentityHttpHost(fixture, null, null, interceptor);
        var response = Assert.IsType<AuthenticationRefused>(await failing.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier));
        Assert.Equal(AuthenticationFailure.Unavailable, response.Code);
        var after = await State();
        if (fault == "lost-response")
        {
            await AssertActivated(ready.Challenge, before.SecurityVersion + 1, before.FactorGeneration + 1);
            Assert.IsType<AuthenticationRefused>(await setup.ConfirmFactorAsync(ready.Challenge.Handle!, Code(ready.Challenge), ready.Verifier));
        }
        else
        {
            Assert.Equal(before.SecurityVersion, after.SecurityVersion);
            Assert.Equal(before.FactorGeneration, after.FactorGeneration);
            Assert.Equal(before.LocalMfaRecoveryRequired, after.LocalMfaRecoveryRequired);
            Assert.Equal(before.ProtectedTotpSecret, after.ProtectedTotpSecret);
            Assert.Equal(before.LastAcceptedTotpStep, after.LastAcceptedTotpStep);
            await using var db = fixture.CreateContext();
            Assert.False(await db.Set<AuthenticationAuditEvent>().AnyAsync(x => x.UserID == fixture.UserID &&
                (x.Outcome == "MfaEnrolled" || x.Outcome == "MfaReplaced" || x.Outcome == "MfaRecovered")));
        }
    }

    [Theory]
    [InlineData("before-save")]
    [InlineData("after-save")]
    [InlineData("lost-response")]
    public async Task Admin_reset_and_code_issuance_commit_together_or_roll_back_together(string fault)
    {
        await fixture.ResetAsync(mfa: true);
        using var setup = new IdentityHttpHost(fixture); var admin = await AdminLogin(setup);
        var before = await State();
        IInterceptor interceptor = fault == "lost-response" ? new LostCommitResponse() : new RecoveryIssuanceFault(fault == "after-save");
        using var failing = new IdentityHttpHost(fixture, null, null, interceptor);
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await failing.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic fault check")).Code);
        var after = await State();
        await using var db = fixture.CreateContext();
        if (fault == "lost-response")
        {
            Assert.True(after.LocalMfaRecoveryRequired); Assert.Equal(2, after.SecurityVersion); Assert.Null(after.ProtectedTotpSecret);
            Assert.Single(await db.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.MfaRecovery).ToListAsync());
            Assert.IsType<MfaRecoveryCodeIssued>(await setup.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic recovery after lost response"));
            Assert.Equal(2, (await State()).SecurityVersion);
        }
        else
        {
            Assert.False(after.LocalMfaRecoveryRequired); Assert.Equal(1, after.SecurityVersion);
            Assert.Equal(before.ProtectedTotpSecret, after.ProtectedTotpSecret);
            Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.MfaRecovery));
            Assert.False(await db.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "MfaReset" || x.Outcome == "MfaRecoveryIssued"));
        }
    }

    private async Task<PreparedFactor> PrepareKind(IdentityHttpHost host, string kind)
    {
        if (kind == "enroll") return await PrepareFactor(host);
        await fixture.ResetAsync(mfa: true);
        if (kind == "replace") return await PrepareFactor(host, replace: true);
        var old = await Login(host, mfa: true); var admin = await AdminLogin(host);
        var root = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic recovery check"));
        var pkce = IdentityHttpHost.Pkce();
        var child = Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, root.Code, pkce.Challenge), AuthenticationStep.NewMfa);
        return new(child, pkce.Verifier, old);
    }

    [Fact]
    public async Task Recovery_permission_revoked_after_token_validation_prevents_reset()
    {
        await fixture.ResetAsync(mfa: true);
        using var setup = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(setup); var before = await State();
        using var gate = new AdmissionGate("MfaRecoveryAdminProof");
        using var host = new IdentityHttpHost(fixture, observe: gate.Observe);
        var issuing = host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic revoked permission check");
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = fixture.CreateContext();
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, (string?)null));
        }
        finally { gate.Release(); }
        Assert.IsType<AuthenticationRefused>(await issuing);
        Assert.Equal(before.ProtectedTotpSecret, (await State()).ProtectedTotpSecret);
        Assert.Equal(before.SecurityVersion, (await State()).SecurityVersion);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "MfaReset" || x.Outcome == "MfaRecoveryIssued"));
    }

    [Theory]
    [InlineData("before-save")]
    [InlineData("after-save")]
    [InlineData("lost-response")]
    public async Task Required_password_and_new_factor_commit_as_one_security_change(string fault)
    {
        await using (var db = fixture.CreateContext())
        {
            await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.MfaMandatory, true));
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.RequireChangePassword, true));
        }
        using var setup = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await setup.LoginAsync(fixture, pkce.Challenge), AuthenticationStep.PasswordChange);
        var pending = Challenge(await setup.ChangePasswordAsync(start.Handle!, PasswordChangeSqlTests.NewPassword, pkce.Verifier), AuthenticationStep.NewMfa);
        IInterceptor interceptor = fault == "lost-response" ? new LostCommitResponse() : new CompletionSaveFault(fault == "after-save");
        using var failing = new IdentityHttpHost(fixture, null, null, interceptor);
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await failing.ConfirmFactorAsync(pending.Handle!, Code(pending), pkce.Verifier)).Code);
        await using var verify = fixture.CreateContext();
        var user = await verify.Users.SingleAsync(x => x.ID == fixture.UserID);
        if (fault == "lost-response")
        {
            Assert.True(HashService.VerifyVersionedPassword(PasswordChangeSqlTests.NewPassword, user.Salt, user.PasswordHash));
            Assert.False(user.RequireChangePassword);
            await AssertActivated(pending, 2, 2);
            Assert.IsType<AuthenticationRefused>(await setup.ConfirmFactorAsync(pending.Handle!, Code(pending), pkce.Verifier));
        }
        else
        {
            Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
            Assert.True(user.RequireChangePassword); Assert.Null((await State()).ProtectedTotpSecret);
            Assert.Equal(1, (await State()).SecurityVersion);
            Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "PasswordChanged" || x.Outcome == "MfaEnrolled"));
        }
    }

    private sealed class RecoveryIssuanceFault(bool afterSave) : SaveChangesInterceptor
    {
        private void Fail(DbContext? context, bool after)
        {
            if (afterSave == after && context?.ChangeTracker.Entries<AuthenticationAuditEvent>().Any(x => x.Entity.Outcome == "MfaRecoveryIssued") == true)
                throw new IOException("Synthetic recovery issuance failure.");
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, false); return ValueTask.FromResult(result); }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        { Fail(eventData.Context, true); return ValueTask.FromResult(result); }
    }
}
