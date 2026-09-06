using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class AdmissionConcurrencySqlTests(SqlIdentityFixture fixture)
{
    [Fact]
    public async Task Password_proof_cannot_be_promoted_after_a_version_change()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        using var gate = new AdmissionGate("PasswordProof");
        using var host = new IdentityHttpHost(fixture, observe: gate.Observe);
        var login = host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var writer = fixture.CreateContext();
            await writer.Set<UserSecurityState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
        }
        finally { gate.Release(); }
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await login).Code);
    }

    [Fact]
    public async Task Refresh_admitted_before_mutation_returns_only_old_version_and_bounded_access()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        using var initialHost = new IdentityHttpHost(fixture);
        var initial = Assert.IsType<SessionIssued>(await initialHost.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Session;
        using var gate = new AdmissionGate("AdmissionLock");
        using var host = new IdentityHttpHost(fixture, observe: gate.Observe);
        var refresh = host.RefreshAsync(initial.RefreshToken);
        Task<int>? mutation = null;
        var command = new SqlCommandSignal("UPDATE");
        await using var writer = fixture.CreateContext(command);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            mutation = writer.Set<UserSecurityState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
            await command.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(mutation.IsCompleted);
        }
        finally { gate.Release(); }
        var result = Assert.IsType<SessionIssued>(await refresh);
        await mutation!;
        var access = new JwtSecurityTokenHandler().ReadJwtToken(result.Session.Token);
        Assert.Contains(access.Claims, x => x.Type == "shift_sv" && x.Value == "1");
        Assert.True(access.ValidTo <= DateTime.UtcNow.AddMinutes(15));
        Assert.Equal(AuthenticationFailure.StaleOperation,
            Assert.IsType<AuthenticationRefused>(await initialHost.RefreshAsync(result.Session.RefreshToken)).Code);
    }

    [Fact]
    public async Task Mutation_admitted_before_refresh_refuses_old_credentials()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        using var initialHost = new IdentityHttpHost(fixture);
        var initial = Assert.IsType<SessionIssued>(await initialHost.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Session;
        await using var writer = fixture.CreateContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Set<UserSecurityState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
        var command = new SqlCommandSignal("UPDLOCK");
        using var host = new IdentityHttpHost(fixture, null, null, command);
        var refresh = host.RefreshAsync(initial.RefreshToken);
        await command.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(refresh.IsCompleted);
        await transaction.CommitAsync();
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await refresh).Code);
    }

    [Fact]
    public async Task Concurrent_mfa_requests_overlap_at_the_database_lock_and_only_one_completes()
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        await fixture.ResetAsync(true);
        using var setup = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await setup.LoginAsync(fixture, pkce.Challenge)).Challenge;
        using var gate = new AdmissionGate("AdmissionLock");
        using var first = new IdentityHttpHost(fixture, observe: gate.Observe);
        var command = new SqlCommandSignal("UPDLOCK");
        using var second = new IdentityHttpHost(fixture, null, null, command);
        var code = new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
        var a = first.CompleteAsync(challenge.Handle!, code, pkce.Verifier);
        Task<AuthOutcome>? b = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            b = second.CompleteAsync(challenge.Handle!, code, pkce.Verifier);
            await command.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(b.IsCompleted);
        }
        finally { gate.Release(); }
        Assert.IsType<SessionIssued>(await a);
        Assert.IsType<AuthenticationRefused>(await b!);
        fixture.Clock = TimeProvider.System;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_failure_rolls_back_consumption_factor_and_audit(bool afterSave)
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        await fixture.ResetAsync(true);
        using var setup = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await setup.LoginAsync(fixture, pkce.Challenge)).Challenge;
        var fault = new CompletionSaveFault(afterSave);
        using var host = new IdentityHttpHost(fixture, null, null, fault);
        var code = new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(AuthenticationFailure.Unavailable,
            Assert.IsType<AuthenticationRefused>(await host.CompleteAsync(challenge.Handle!, code, pkce.Verifier)).Code);
        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(AuthenticationOperationState.AwaitingMfa, (await db.Set<AuthenticationOperation>().SingleAsync()).State);
            Assert.Null((await db.Set<UserSecurityState>().SingleAsync()).LastAcceptedTotpStep);
            Assert.DoesNotContain(await db.Set<AuthenticationAuditEvent>().ToListAsync(), x => x.Outcome is "MfaCompleted" or "SessionIssued");
        }
        Assert.IsType<SessionIssued>(await host.CompleteAsync(challenge.Handle!, code, pkce.Verifier));
        fixture.Clock = TimeProvider.System;
    }

    [Fact]
    public async Task Lost_commit_response_cannot_replay_for_another_session()
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        await fixture.ResetAsync(true);
        using var setup = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await setup.LoginAsync(fixture, pkce.Challenge)).Challenge;
        using var host = new IdentityHttpHost(fixture, null, null, new LostCommitResponse());
        var code = new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
        Assert.Equal(AuthenticationFailure.Unavailable,
            Assert.IsType<AuthenticationRefused>(await host.CompleteAsync(challenge.Handle!, code, pkce.Verifier)).Code);
        Assert.IsType<AuthenticationRefused>(await setup.CompleteAsync(challenge.Handle!, code, pkce.Verifier));
        await using var db = fixture.CreateContext();
        Assert.Equal(AuthenticationOperationState.Completed, (await db.Set<AuthenticationOperation>().SingleAsync()).State);
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "SessionIssued").ToListAsync());
        fixture.Clock = TimeProvider.System;
    }
}
