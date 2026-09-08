using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Tests;

[Trait("Category", "Sql")]
public sealed partial class MfaLifecycleSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string AdminName = "synthetic-recovery-admin";
    private const string RecoveryPermission = "{\"ShiftIdentityActions\":{\"ManageMfaRecovery\":[\"m\"]}}";
    private ControlledClock clock = null!;
    private long adminID;

    public async ValueTask InitializeAsync()
    {
        clock = new(DateTimeOffset.UtcNow); fixture.Clock = clock;
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        var actor = await db.Users.SingleOrDefaultAsync(x => x.Username == AdminName);
        adminID = actor?.ID ?? await fixture.CreateSyntheticUserAsync(AdminName, RecoveryPermission, mfa: true);
        await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, RecoveryPermission).SetProperty(u => u.IsActive, true));
        await db.Set<UserSecurityState>().Where(x => x.UserID == adminID).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.SecurityVersion, 1).SetProperty(s => s.LastAcceptedTotpStep, (long?)null)
            .SetProperty(s => s.FailedProofs, 0).SetProperty(s => s.LocalMfaRecoveryRequired, false));
        Assert.True(new TypeAuthContext(RecoveryPermission, typeof(ShiftIdentityActions)).CanAccess(ShiftIdentityActions.ManageMfaRecovery));
    }

    public ValueTask DisposeAsync() { fixture.Clock = TimeProvider.System; return ValueTask.CompletedTask; }

    [Fact, Trait("Category", "Http")]
    public async Task First_enrollment_requires_current_password_and_new_factor_then_invalidates_old_renewal()
    {
        using var host = new IdentityHttpHost(fixture);
        var old = await Login(host);
        var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await host.StartMfaAsync(old.Session.Token, pkce.Challenge), AuthenticationStep.Password);
        Assert.Null(start.NewAuthenticator);
        Assert.IsType<AuthenticationRefused>(await host.MfaPasswordAsync(start.Handle!, "wrong password", pkce.Verifier));
        var setup = Challenge(await host.MfaPasswordAsync(start.Handle!, fixture.Password, pkce.Verifier), AuthenticationStep.NewMfa);
        Assert.NotEqual(start.Handle, setup.Handle);
        Assert.Equal(start.ExpiresAt, setup.ExpiresAt);
        Assert.Null((await State()).ProtectedTotpSecret);
        Assert.IsType<AuthenticationRefused>(await host.MfaPasswordAsync(start.Handle!, fixture.Password, pkce.Verifier));
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(setup.Handle!, "00000000", pkce.Verifier));
        var code = Code(setup);
        var changed = Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(setup.Handle!, code, pkce.Verifier));
        var session = Assert.IsType<SessionIssued>(changed.Continuation);
        await AssertActivated(setup, 2, 2);
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(old.Session.RefreshToken));
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(setup.Handle!, code, pkce.Verifier));
        using var otherHost = new IdentityHttpHost(fixture);
        var login = Challenge(await otherHost.LoginAsync(fixture, pkce.Challenge), AuthenticationStep.ExistingMfa);
        Assert.IsType<AuthenticationRefused>(await otherHost.CompleteAsync(login.Handle!, code, pkce.Verifier));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.IsType<SessionIssued>(await otherHost.CompleteAsync(login.Handle!, Code(setup), pkce.Verifier));
    }

    [Theory, Trait("Category", "Http")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mandatory_enrollment_is_usable_with_and_without_required_password_change(bool passwordChange)
    {
        await using (var db = fixture.CreateContext())
        {
            await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.MfaMandatory, true));
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.RequireChangePassword, passwordChange));
        }
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge)).Challenge;
        if (passwordChange)
        {
            Assert.Equal(AuthenticationStep.PasswordChange, challenge.Step);
            challenge = Challenge(await host.ChangePasswordAsync(challenge.Handle!, PasswordChangeSqlTests.NewPassword, pkce.Verifier), AuthenticationStep.NewMfa);
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            Assert.True(HashService.VerifyVersionedPassword(fixture.Password, user.Salt, user.PasswordHash));
        }
        Assert.Equal(AuthenticationStep.NewMfa, challenge.Step);
        var changed = Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(challenge.Handle!, Code(challenge), pkce.Verifier));
        Assert.IsType<SessionIssued>(changed.Continuation);
        Assert.Equal(passwordChange, changed.PasswordAlsoChanged);
        await AssertActivated(challenge, 2, 2);
        await using var verify = fixture.CreateContext();
        var saved = await verify.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.False(saved.RequireChangePassword);
        Assert.True(HashService.VerifyVersionedPassword(passwordChange ? PasswordChangeSqlTests.NewPassword : fixture.Password, saved.Salt, saved.PasswordHash));
    }

    [Theory, Trait("Category", "Http")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Replacement_requires_the_old_factor_and_keeps_it_active_until_confirmation(bool cancel)
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var old = await Login(host, mfa: true);
        var pkce = IdentityHttpHost.Pkce();
        var start = Challenge(await host.StartMfaAsync(old.Session.Token, pkce.Challenge, replace: true), AuthenticationStep.ExistingMfa);
        Assert.IsType<AuthenticationRefused>(await host.MfaPasswordAsync(start.Handle!, fixture.Password, pkce.Verifier));
        Assert.IsType<AuthenticationRefused>(await host.ExistingFactorAsync(start.Handle!, ActiveCode(), pkce.Verifier));
        clock.Advance(TimeSpan.FromSeconds(30));
        var setup = Challenge(await host.ExistingFactorAsync(start.Handle!, ActiveCode(), pkce.Verifier), AuthenticationStep.NewMfa);
        Assert.Equal(start.ExpiresAt, setup.ExpiresAt);
        var active = await State();
        Assert.Equal(fixture.FactorSecret, fixture.Protection.CreateProtector("Identity.Totp.v2").Unprotect(active.ProtectedTotpSecret!));
        Assert.Equal(1, active.SecurityVersion);
        Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(setup.Handle!, "00000000", pkce.Verifier));
        if (cancel)
        {
            Assert.IsType<OperationCancelled>(await host.CancelAsync(setup.Handle!, pkce.Verifier));
            Assert.Equal(active.ProtectedTotpSecret, (await State()).ProtectedTotpSecret);
            Assert.IsType<SessionIssued>(await host.RefreshAsync(old.Session.RefreshToken));
            Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(setup.Handle!, Code(setup), pkce.Verifier));
            await AssertNoPendingMaterial();
        }
        else
        {
            var changed = Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(setup.Handle!, Code(setup), pkce.Verifier));
            var session = Assert.IsType<SessionIssued>(changed.Continuation);
            await AssertActivated(setup, 2, 2);
            Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(old.Session.RefreshToken));
            Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        }
    }

    [Fact, Trait("Category", "Http")]
    public async Task Admin_recovery_requires_both_proofs_and_returns_to_login_even_when_mfa_is_optional()
    {
        await fixture.ResetAsync(mfa: true);
        using var host = new IdentityHttpHost(fixture);
        var old = await Login(host, mfa: true);
        var admin = await AdminLogin(host);
        var recovery = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic check 42"));
        var reset = await State();
        Assert.True(reset.LocalMfaRecoveryRequired); Assert.Null(reset.ProtectedTotpSecret);
        Assert.Equal(2, reset.SecurityVersion); Assert.Equal(2, reset.FactorGeneration);
        Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(old.Session.RefreshToken));
        var pkce = IdentityHttpHost.Pkce();
        Challenge(await host.LoginAsync(fixture, pkce.Challenge), AuthenticationStep.MfaRecovery);
        Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, "wrong", recovery.Code, pkce.Challenge));
        Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, "WRONG", pkce.Challenge));
        var setup = Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, recovery.Code.ToLowerInvariant().Replace('-', ' '), pkce.Challenge), AuthenticationStep.NewMfa);
        Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, recovery.Code, pkce.Challenge));
        var result = Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(setup.Handle!, Code(setup), pkce.Verifier));
        Assert.IsType<ReturnToLogin>(result.Continuation);
        await AssertActivated(setup, 3, 3);
        Assert.False((await State()).LocalMfaRecoveryRequired);
        clock.Advance(TimeSpan.FromSeconds(30));
        var login = Challenge(await host.LoginAsync(fixture, pkce.Challenge), AuthenticationStep.ExistingMfa);
        Assert.IsType<SessionIssued>(await host.CompleteAsync(login.Handle!, Code(setup), pkce.Verifier));
        await using var verify = fixture.CreateContext();
        var issued = await verify.Set<AuthenticationAuditEvent>().SingleAsync(x => x.Outcome == "MfaRecoveryIssued");
        Assert.Equal(adminID, issued.ActorUserID); Assert.Equal("Synthetic check 42", issued.VerificationReference);
        Assert.DoesNotContain(recovery.Code, System.Text.Json.JsonSerializer.Serialize(await verify.Set<AuthenticationOperation>().ToListAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reissue_supersedes_the_old_code_and_child_without_an_extra_version_bump(bool createChild)
    {
        using var host = new IdentityHttpHost(fixture);
        var admin = await AdminLogin(host);
        var first = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic check 1"));
        var pkce = IdentityHttpHost.Pkce();
        var child = createChild ? Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, first.Code, pkce.Challenge), AuthenticationStep.NewMfa) : null;
        var second = Assert.IsType<MfaRecoveryCodeIssued>(await host.IssueRecoveryAsync(admin.Session.Token, fixture.UserID, "Synthetic check 2"));
        Assert.NotEqual(first.Code, second.Code); Assert.Equal(2, (await State()).SecurityVersion);
        Assert.IsType<AuthenticationRefused>(await host.RecoverMfaAsync(fixture.Username, fixture.Password, first.Code, pkce.Challenge));
        if (child is not null) Assert.IsType<AuthenticationRefused>(await host.ConfirmFactorAsync(child.Handle!, Code(child), pkce.Verifier));
        Challenge(await host.RecoverMfaAsync(fixture.Username, fixture.Password, second.Code, pkce.Challenge), AuthenticationStep.NewMfa);
        await using var db = fixture.CreateContext();
        Assert.All(await db.Set<AuthenticationOperation>().Where(x => x.State == AuthenticationOperationState.Superseded).ToListAsync(), x =>
        { Assert.Null(x.ProtectedPendingTotpSecret); Assert.Null(x.RecoveryCodeDigest); Assert.Null(x.OutstandingRecoveryUserID); });
    }

    private async Task<SessionIssued> Login(IdentityHttpHost host, bool mfa = false)
    {
        var pkce = IdentityHttpHost.Pkce();
        var result = await host.LoginAsync(fixture, pkce.Challenge);
        if (mfa) result = await host.CompleteAsync(Challenge(result, AuthenticationStep.ExistingMfa).Handle!, ActiveCode(), pkce.Verifier);
        return Assert.IsType<SessionIssued>(result);
    }

    private async Task<SessionIssued> AdminLogin(IdentityHttpHost host)
    {
        var pkce = IdentityHttpHost.Pkce();
        var response = await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login", new PasswordLoginRequest(AdminName, fixture.Password, pkce.Challenge)));
        var challenge = Challenge(response, AuthenticationStep.ExistingMfa);
        return Assert.IsType<SessionIssued>(await host.CompleteAsync(challenge.Handle!, ActiveCode(), pkce.Verifier));
    }

    private static AuthenticationChallenge Challenge(AuthOutcome outcome, AuthenticationStep step)
    {
        var challenge = Assert.IsType<ChallengeRequired>(outcome).Challenge;
        Assert.Equal(step, challenge.Step);
        if (step == AuthenticationStep.NewMfa) Assert.NotNull(challenge.NewAuthenticator);
        return challenge;
    }

    private string ActiveCode() => new Totp(fixture.FactorSecret).ComputeTotp(clock.GetUtcNow().UtcDateTime);
    private string Code(AuthenticationChallenge setup) => new Totp(Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret)).ComputeTotp(clock.GetUtcNow().UtcDateTime);
    private async Task<UserSecurityState> State()
    {
        await using var db = fixture.CreateContext();
        return await db.Set<UserSecurityState>().AsNoTracking().SingleAsync(x => x.UserID == fixture.UserID);
    }
    private async Task AssertActivated(AuthenticationChallenge setup, long version, long generation)
    {
        var security = await State();
        Assert.Equal(version, security.SecurityVersion); Assert.Equal(generation, security.FactorGeneration);
        Assert.Equal(1, security.TotpProtectionVersion); Assert.NotNull(security.LastAcceptedTotpStep);
        var secret = fixture.Protection.CreateProtector("Identity.Totp.v2").CreateProtector($"Active.v1:{fixture.UserID}:{generation}").Unprotect(security.ProtectedTotpSecret!);
        Assert.Equal(Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret), secret); CryptographicOperations.ZeroMemory(secret);
        await AssertNoPendingMaterial();
    }
    private async Task AssertNoPendingMaterial()
    {
        await using var db = fixture.CreateContext();
        Assert.All(await db.Set<AuthenticationOperation>().Where(x => x.UserID == fixture.UserID &&
            (x.State == AuthenticationOperationState.Completed || x.State == AuthenticationOperationState.Cancelled || x.State == AuthenticationOperationState.Locked)).ToListAsync(), x =>
        { Assert.Null(x.ProtectedPendingTotpSecret); Assert.Null(x.RecoveryCodeDigest); Assert.Null(x.PendingPasswordHash); });
    }
}
