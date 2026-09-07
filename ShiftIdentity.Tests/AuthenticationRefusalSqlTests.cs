using Microsoft.EntityFrameworkCore;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class AuthenticationRefusalSqlTests(SqlIdentityFixture fixture)
{
    [Theory]
    [InlineData("inactive", null, AuthenticationFailure.AccountUnavailable)]
    [InlineData("deleted", null, AuthenticationFailure.AccountUnavailable)]
    [InlineData("forced", AuthenticationStep.PasswordChange, null)]
    [InlineData("mandatory", AuthenticationStep.NewMfa, null)]
    [InlineData("recovery", AuthenticationStep.MfaRecovery, null)]
    [InlineData("email", AuthenticationStep.EmailVerification, null)]
    [InlineData("policy", null, AuthenticationFailure.Unavailable)]
    public async Task Local_conditions_cannot_become_ordinary_sessions(string scenario, AuthenticationStep? step, AuthenticationFailure? failure)
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync();
            var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
            switch (scenario)
            {
                case "inactive": user.IsActive = false; break;
                case "deleted": user.IsDeleted = true; break;
                case "forced": user.RequireChangePassword = true; break;
                case "mandatory": policy.MfaMandatory = true; break;
                case "recovery": state.LocalMfaRecoveryRequired = true; policy.MfaEnabled = false; break;
                case "email": policy.RequireVerifiedEmail = true; user.Email = "saved@example.invalid"; break;
                case "policy": policy.Revision++; break;
            }
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        var outcome = await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge);
        if (step is not null)
        {
            var challenge = Assert.IsType<ChallengeRequired>(outcome).Challenge;
            Assert.Equal(step, challenge.Step);
            if (step == AuthenticationStep.PasswordChange)
            {
                Assert.NotNull(challenge.Handle);
                Assert.Equal(AuthenticationOperationPurpose.PasswordChange, challenge.Purpose);
            }
            else Assert.Null(challenge.Handle); // The other remedies remain restricted.
        }
        else Assert.Equal(failure, Assert.IsType<AuthenticationRefused>(outcome).Code);
    }

    [Fact]
    public async Task Email_gate_exempts_no_email_accounts()
    {
        await fixture.ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            (await db.Set<AuthenticationPolicyState>().SingleAsync()).RequireVerifiedEmail = true;
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
    }

    [Theory]
    [InlineData("client")]
    [InlineData("verifier")]
    [InlineData("purpose")]
    [InlineData("version")]
    [InlineData("factor")]
    [InlineData("expired")]
    [InlineData("access")]
    public async Task Mfa_refuses_unbound_or_stale_credentials(string scenario)
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        await fixture.ResetAsync(true);
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge)).Challenge;
        await using (var db = fixture.CreateContext())
        {
            if (scenario == "purpose") (await db.Set<AuthenticationOperation>().SingleAsync()).Purpose = AuthenticationOperationPurpose.ContactChange;
            if (scenario == "version") (await db.Set<UserSecurityState>().SingleAsync()).SecurityVersion++;
            if (scenario == "factor") (await db.Set<UserSecurityState>().SingleAsync()).FactorGeneration++;
            await db.SaveChangesAsync();
        }
        if (scenario == "expired") clock.Advance(TimeSpan.FromMinutes(5));
        using var other = new IdentityHttpHost(fixture, new("other-client", "other-api"));
        var attemptHost = scenario == "client" ? other : host;
        var handle = scenario == "access" ? "eyJhbGciOiJSUzI1NiJ9.e30.invalid" : challenge.Handle!;
        var verifier = scenario == "verifier" ? IdentityHttpHost.Pkce().Verifier : pkce.Verifier;
        Assert.IsType<AuthenticationRefused>(await attemptHost.CompleteAsync(handle, Code(clock), verifier));
        await using var check = fixture.CreateContext();
        Assert.Null((await check.Set<UserSecurityState>().SingleAsync()).LastAcceptedTotpStep);
        Assert.Equal(AuthenticationOperationState.AwaitingMfa, (await check.Set<AuthenticationOperation>().SingleAsync()).State);
        fixture.Clock = TimeProvider.System;
    }

    [Fact]
    public async Task Same_factor_step_cannot_complete_a_second_operation()
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        await fixture.ResetAsync(true);
        using var first = new IdentityHttpHost(fixture);
        using var second = new IdentityHttpHost(fixture);
        var a = IdentityHttpHost.Pkce(); var b = IdentityHttpHost.Pkce();
        var ca = Assert.IsType<ChallengeRequired>(await first.LoginAsync(fixture, a.Challenge)).Challenge;
        var cb = Assert.IsType<ChallengeRequired>(await second.LoginAsync(fixture, b.Challenge)).Challenge;
        Assert.IsType<SessionIssued>(await first.CompleteAsync(ca.Handle!, Code(fixture.Clock), a.Verifier));
        Assert.IsType<AuthenticationRefused>(await second.CompleteAsync(cb.Handle!, Code(fixture.Clock), b.Verifier));
        fixture.Clock = TimeProvider.System;
    }

    [Fact]
    public async Task Attempts_persist_across_hosts_and_new_operations_do_not_reset_the_budget()
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        await fixture.ResetAsync(true);
        using var first = new IdentityHttpHost(fixture);
        using var second = new IdentityHttpHost(fixture);
        var badCode = Enumerable.Range(0, 1_000_000).Select(x => x.ToString("D6"))
            .First(x => !new Totp(fixture.FactorSecret).VerifyTotp(fixture.Clock.GetUtcNow().UtcDateTime, x, out _, new VerificationWindow(1, 1)));
        for (var operation = 0; operation < 2; operation++)
        {
            var pkce = IdentityHttpHost.Pkce();
            var challenge = Assert.IsType<ChallengeRequired>(await first.LoginAsync(fixture, pkce.Challenge)).Challenge;
            for (var attempt = 0; attempt < 5; attempt++)
                Assert.IsType<AuthenticationRefused>(await (attempt % 2 == 0 ? first : second).CompleteAsync(challenge.Handle!, badCode, pkce.Verifier));
            Assert.Equal(AuthenticationFailure.AttemptsExhausted,
                Assert.IsType<AuthenticationRefused>(await first.CompleteAsync(challenge.Handle!, Code(fixture.Clock), pkce.Verifier)).Code);
        }
        Assert.Equal(AuthenticationFailure.AttemptsExhausted,
            Assert.IsType<AuthenticationRefused>(await first.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Code);
        await using var db = fixture.CreateContext();
        Assert.Equal(10, (await db.Set<UserSecurityState>().SingleAsync()).FailedProofs);
        Assert.All(await db.Set<AuthenticationOperation>().ToListAsync(), x => Assert.Equal(5, x.FailedAttempts));
        fixture.Clock = TimeProvider.System;
    }

    [Fact]
    public async Task Refresh_does_not_clear_failed_password_attempts()
    {
        await fixture.ResetAsync();
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Session;
        Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, "incorrect"));
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.RefreshToken));
        await using var db = fixture.CreateContext();
        Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync()).FailedProofs);
    }

    [Fact]
    public async Task Missing_security_state_refuses_instead_of_backfilling_during_login()
    {
        await fixture.ResetAsync();
        await using var db = fixture.CreateContext();
        await db.Set<UserSecurityState>().ExecuteDeleteAsync();
        try
        {
            using var host = new IdentityHttpHost(fixture);
            Assert.Equal(AuthenticationFailure.Unavailable,
                Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Code);
            Assert.Empty(await db.Set<UserSecurityState>().ToListAsync());
        }
        finally
        {
            db.Set<UserSecurityState>().Add(new() { UserID = fixture.UserID });
            await db.SaveChangesAsync();
        }
    }

    private string Code(TimeProvider clock) => new Totp(fixture.FactorSecret).ComputeTotp(clock.GetUtcNow().UtcDateTime);
}
