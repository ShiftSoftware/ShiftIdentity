using System.IdentityModel.Tokens.Jwt;
using System.Net;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class PasswordChangeSqlTests(SqlIdentityFixture fixture)
{
    internal const string NewPassword = "A new synthetic phrase 83!";

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [Trait("Category", "Http")]
    public async Task Password_change_journeys_preserve_MFA_and_invalidate_old_renewal(bool required, bool mfa)
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync(mfa);
            using var host = new IdentityHttpHost(fixture);
            var old = await Login(host, mfa);
            var other = Assert.IsType<SessionIssued>(await host.RefreshAsync(old.Session.RefreshToken));
            var pkce = IdentityHttpHost.Pkce();
            AuthenticationChallenge challenge;
            if (required)
            {
                await using var db = fixture.CreateContext();
                await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.RequireChangePassword, true));
                challenge = Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge)).Challenge;
            }
            else
            {
                challenge = Assert.IsType<ChallengeRequired>(await host.StartPasswordChangeAsync(old.Session.Token, pkce.Challenge)).Challenge;
                Assert.Equal(AuthenticationStep.Password, challenge.Step);
                Assert.IsType<AuthenticationRefused>(await host.ChangePasswordAsync(challenge.Handle!, NewPassword, pkce.Verifier));
                var original = challenge;
                challenge = Assert.IsType<ChallengeRequired>(await host.ProvePasswordAsync(challenge.Handle!, fixture.Password, pkce.Verifier)).Challenge;
                Assert.NotEqual(original.Handle, challenge.Handle);
                Assert.Equal(original.ExpiresAt, challenge.ExpiresAt);
                Assert.IsType<AuthenticationRefused>(await host.ProvePasswordAsync(original.Handle!, fixture.Password, pkce.Verifier));
                if (mfa)
                {
                    // The sign-in TOTP is not reusable as fresh password-change proof.
                    Assert.IsType<AuthenticationRefused>(await host.PasswordMfaAsync(challenge.Handle!, Code(), pkce.Verifier));
                    clock.Advance(TimeSpan.FromSeconds(30));
                    challenge = Assert.IsType<ChallengeRequired>(await host.PasswordMfaAsync(challenge.Handle!, Code(), pkce.Verifier)).Challenge;
                }
            }
            Assert.Equal(AuthenticationStep.PasswordChange, challenge.Step);
            var outcome = await host.ChangePasswordAsync(challenge.Handle!, NewPassword, pkce.Verifier);
            if (required && mfa)
            {
                var pending = Assert.IsType<ChallengeRequired>(outcome).Challenge;
                Assert.Equal(AuthenticationStep.ExistingMfa, pending.Step);
                await AssertState(1, fixture.Password, true);
                Assert.IsType<AuthenticationRefused>(await host.PasswordMfaAsync(pending.Handle!, "00000000", pkce.Verifier));
                await AssertState(1, fixture.Password, true);
                clock.Advance(TimeSpan.FromSeconds(30));
                outcome = await host.PasswordMfaAsync(pending.Handle!, Code(), pkce.Verifier);
                Assert.IsType<AuthenticationRefused>(await host.PasswordMfaAsync(pending.Handle!, Code(), pkce.Verifier));
            }
            var changed = Assert.IsType<PasswordChanged>(outcome);
            var session = Assert.IsType<SessionIssued>(changed.Continuation);
            Assert.Equal("2", new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token).Claims.Single(c => c.Type == "shift_sv").Value);
            await AssertState(2, NewPassword, false);
            Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
            Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(old.Session.RefreshToken));
            Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(other.Session.RefreshToken));
            Assert.IsType<AuthenticationRefused>(await host.StartPasswordChangeAsync(old.Session.Token, pkce.Challenge));
            Assert.IsType<AuthenticationRefused>(await host.ChangePasswordAsync(challenge.Handle!, NewPassword, pkce.Verifier));
            Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, pkce.Challenge));
            var fresh = await host.LoginAsync(fixture, pkce.Challenge, NewPassword);
            if (mfa) Assert.Equal(AuthenticationStep.ExistingMfa, Assert.IsType<ChallengeRequired>(fresh).Challenge.Step);
            else Assert.IsType<SessionIssued>(fresh);
            host.Client.DefaultRequestHeaders.Authorization = new("Bearer", old.Session.Token);
            using var residual = await host.Client.GetAsync("/fixture/resource");
            Assert.Equal(HttpStatusCode.OK, residual.StatusCode);
            clock.Advance(TimeSpan.FromMinutes(15));
            using var expired = await host.Client.GetAsync("/fixture/resource");
            Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
            await using var verify = fixture.CreateContext();
            var security = await verify.Set<UserSecurityState>().SingleAsync();
            Assert.Equal(mfa, security.ProtectedTotpSecret is not null);
            Assert.Equal(1, security.FactorGeneration);
            Assert.Equal(1, await verify.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "PasswordChanged"));
            Assert.All(await verify.Set<AuthenticationOperation>().Where(x => x.State == AuthenticationOperationState.Completed).ToListAsync(), op =>
            { Assert.Empty(op.HandleDigest); Assert.Null(op.PendingPasswordHash); Assert.Null(op.PendingPasswordSalt); });
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Theory]
    [InlineData("short", PasswordPolicyFailure.TooShort)]
    [InlineData("passwordpassword", PasswordPolicyFailure.Blocked)]
    [InlineData("Synthetic Password 7!", PasswordPolicyFailure.SameAsCurrent)]
    public async Task Invalid_new_password_preserves_the_operation_and_existing_credential(string value, PasswordPolicyFailure reason)
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        using var host = new IdentityHttpHost(fixture);
        var (challenge, verifier) = await Required(host);
        var refusal = Assert.IsType<AuthenticationRefused>(await host.ChangePasswordAsync(challenge.Handle!, value, verifier));
        Assert.Equal(AuthenticationFailure.InvalidNewPassword, refusal.Code);
        Assert.Equal(reason, refusal.PasswordFailure);
        await AssertState(1, fixture.Password, true);
        Assert.IsType<PasswordChanged>(await host.ChangePasswordAsync(challenge.Handle!, NewPassword, verifier));
    }

    [Theory]
    [InlineData("recovery", AuthenticationStep.MfaRecovery, false)]
    [InlineData("mandatory", AuthenticationStep.NewMfa, false)]
    [InlineData("email", AuthenticationStep.EmailVerification, true)]
    public async Task Remaining_requirements_are_not_erased(string condition, AuthenticationStep step, bool changed)
    {
        await fixture.ResetAsync();
        using var host = new IdentityHttpHost(fixture);
        var (challenge, verifier) = await Required(host);
        await using (var db = fixture.CreateContext())
        {
            var state = await db.Set<UserSecurityState>().SingleAsync();
            var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
            if (condition == "recovery") state.LocalMfaRecoveryRequired = true;
            if (condition == "mandatory") policy.MfaMandatory = true;
            if (condition == "email")
            { policy.RequireVerifiedEmail = true; (await db.Users.SingleAsync(x => x.ID == fixture.UserID)).Email = "synthetic@example.invalid"; }
            await db.SaveChangesAsync();
        }
        var outcome = await host.ChangePasswordAsync(challenge.Handle!, NewPassword, verifier);
        var continuation = changed ? Assert.IsType<PasswordChanged>(outcome).Continuation : outcome;
        var pending = Assert.IsType<ChallengeRequired>(continuation).Challenge;
        Assert.Equal(step, pending.Step); Assert.Equal(challenge.ExpiresAt, pending.ExpiresAt);
        if (condition == "mandatory") { Assert.NotNull(pending.Handle); Assert.NotNull(pending.NewAuthenticator); }
        else Assert.Null(pending.Handle);
        await AssertState(changed ? 2 : 1, changed ? NewPassword : fixture.Password, !changed);
        await using var verify = fixture.CreateContext();
        Assert.Equal(condition == "recovery", (await verify.Set<UserSecurityState>().SingleAsync()).LocalMfaRecoveryRequired);
    }

    [Fact]
    [Trait("Category", "Http")]
    public async Task Bindings_reject_wrong_purpose_client_user_verifier_and_access_kind()
    {
        await fixture.ResetAsync();
        using var host = new IdentityHttpHost(fixture);
        var session = await Login(host, false);
        var pkce = IdentityHttpHost.Pkce();
        Assert.IsType<AuthenticationRefused>(await host.StartPasswordChangeAsync(session.Session.RefreshToken, pkce.Challenge));
        using var other = new IdentityHttpHost(fixture, new("other-client", "other-api"));
        Assert.IsType<AuthenticationRefused>(await other.StartPasswordChangeAsync(session.Session.Token, pkce.Challenge));
        var (challenge, verifier) = await Required(host);
        Assert.IsType<AuthenticationRefused>(await host.CompleteAsync(challenge.Handle!, "123456", verifier));
        Assert.IsType<AuthenticationRefused>(await other.ChangePasswordAsync(challenge.Handle!, NewPassword, verifier));
        Assert.IsType<AuthenticationRefused>(await host.ChangePasswordAsync(challenge.Handle!, NewPassword, pkce.Verifier));
        var proof = new SessionProof(fixture.UserID, 1, 1, 1, false, fixture.Clock.GetUtcNow(), "test-client", "test-api", false, "another-user");
        var token = new AdmissionTokenCodec(fixture.Options, fixture.Clock).Issue(new(proof, fixture.Username, "Synthetic", [], fixture.Clock.GetUtcNow()));
        Assert.IsType<AuthenticationRefused>(await host.StartPasswordChangeAsync(token.Token, pkce.Challenge));
        Assert.IsType<PasswordChanged>(await host.ChangePasswordAsync(challenge.Handle!, NewPassword, verifier));
    }

    [Fact]
    public async Task Cancellation_discards_pending_hash_and_requires_fresh_login()
    {
        await fixture.ResetAsync(true);
        using var host = new IdentityHttpHost(fixture);
        var (challenge, verifier) = await Required(host);
        var pending = Assert.IsType<ChallengeRequired>(await host.ChangePasswordAsync(challenge.Handle!, NewPassword, verifier)).Challenge;
        Assert.IsType<OperationCancelled>(await host.CancelAsync(pending.Handle!, verifier));
        Assert.IsType<AuthenticationRefused>(await host.PasswordMfaAsync(pending.Handle!, Code(), verifier));
        await AssertState(1, fixture.Password, true);
        await using var db = fixture.CreateContext();
        var op = await db.Set<AuthenticationOperation>().SingleAsync();
        Assert.Equal(AuthenticationOperationState.Cancelled, op.State); Assert.Null(op.PendingPasswordHash);
        Assert.Null((await db.Set<UserSecurityState>().SingleAsync()).LastAcceptedTotpStep);
    }

    [Fact]
    public async Task Original_deadline_and_access_expiry_bound_voluntary_proof_even_after_rotation()
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow); fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync(); using var host = new IdentityHttpHost(fixture);
            var session = await Login(host, false); clock.Advance(TimeSpan.FromMinutes(14));
            var pkce = IdentityHttpHost.Pkce();
            var start = Assert.IsType<ChallengeRequired>(await host.StartPasswordChangeAsync(session.Session.Token, pkce.Challenge)).Challenge;
            var next = Assert.IsType<ChallengeRequired>(await host.ProvePasswordAsync(start.Handle!, fixture.Password, pkce.Verifier)).Challenge;
            Assert.Equal(start.ExpiresAt, next.ExpiresAt);
            clock.Advance(TimeSpan.FromMinutes(1));
            Assert.Equal(AuthenticationFailure.Expired, Assert.IsType<AuthenticationRefused>(await host.ChangePasswordAsync(next.Handle!, NewPassword, pkce.Verifier)).Code);
            await AssertState(1, fixture.Password, false);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Wrong_current_password_budget_survives_new_operations_and_legacy_upgrade_is_version_neutral()
    {
        await fixture.ResetAsync(); using var host = new IdentityHttpHost(fixture);
        var session = await Login(host, false);
        await using (var db = fixture.CreateContext())
        { Assert.False(VersionedPasswordHash.NeedsUpgrade((await db.Users.SingleAsync(x => x.ID == fixture.UserID)).PasswordHash)); }
        for (var i = 0; i < 2; i++)
        {
            var pkce = IdentityHttpHost.Pkce();
            var op = Assert.IsType<ChallengeRequired>(await host.StartPasswordChangeAsync(session.Session.Token, pkce.Challenge)).Challenge;
            for (var j = 0; j < 5; j++) Assert.IsType<AuthenticationRefused>(await host.ProvePasswordAsync(op.Handle!, "incorrect", pkce.Verifier));
            Assert.Equal(AuthenticationFailure.AttemptsExhausted, Assert.IsType<AuthenticationRefused>(await host.ProvePasswordAsync(op.Handle!, fixture.Password, pkce.Verifier)).Code);
        }
        await AssertState(1, fixture.Password, false);
        await using var verify = fixture.CreateContext(); Assert.Equal(10, (await verify.Set<UserSecurityState>().SingleAsync()).FailedProofs);
    }

    private async Task<SessionIssued> Login(IdentityHttpHost host, bool mfa)
    {
        var pkce = IdentityHttpHost.Pkce(); var result = await host.LoginAsync(fixture, pkce.Challenge);
        return mfa ? Assert.IsType<SessionIssued>(await host.CompleteAsync(Assert.IsType<ChallengeRequired>(result).Challenge.Handle!, Code(), pkce.Verifier))
            : Assert.IsType<SessionIssued>(result);
    }
    private string Code() => new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
    private async Task<(AuthenticationChallenge Challenge, string Verifier)> Required(IdentityHttpHost host)
    {
        await using var db = fixture.CreateContext();
        await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.RequireChangePassword, true));
        var pkce = IdentityHttpHost.Pkce();
        return (Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge)).Challenge, pkce.Verifier);
    }
    private async Task AssertState(long version, string password, bool required)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.Equal(version, (await db.Set<UserSecurityState>().SingleAsync()).SecurityVersion);
        Assert.True(HashService.VerifyVersionedPassword(password, user.Salt, user.PasswordHash));
        Assert.Equal(required, user.RequireChangePassword);
    }
}
