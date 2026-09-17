using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class PasswordChangeConcurrencySqlTests(SqlIdentityFixture fixture)
{
    private const string NewPassword = PasswordChangeSqlTests.NewPassword;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_password_change_and_refresh_serialize_in_both_lock_orders(bool changeFirst)
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        using var setup = new IdentityHttpHost(fixture);
        var ready = await Prepare(setup);
        using var gate = new AdmissionGate(changeFirst ? "PasswordMutation" : "AdmissionLock");
        using var first = new IdentityHttpHost(fixture, observe: gate.Observe);
        var signal = new SqlCommandSignal("UPDLOCK");
        using var second = new IdentityHttpHost(fixture, null, null, signal);
        var a = changeFirst ? first.ChangePasswordAsync(ready.Handle, NewPassword, ready.Verifier) : first.RefreshAsync(ready.Session.Session.RefreshToken);
        Task<AuthOutcome>? b = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            b = changeFirst ? second.RefreshAsync(ready.Session.Session.RefreshToken) : second.ChangePasswordAsync(ready.Handle, NewPassword, ready.Verifier);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(b.IsCompleted);
        }
        finally { gate.Release(); }
        var firstResult = await a; var secondResult = await b!;
        var changed = Assert.IsType<PasswordChanged>(changeFirst ? firstResult : secondResult);
        var issued = Assert.IsType<SessionIssued>(changed.Continuation);
        Assert.IsType<SessionIssued>(await setup.RefreshAsync(issued.Session.RefreshToken));
        var refreshResult = changeFirst ? secondResult : firstResult;
        if (changeFirst) Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(refreshResult).Code);
        else
        {
            var old = Assert.IsType<SessionIssued>(refreshResult);
            Assert.Equal("1", new JwtSecurityTokenHandler().ReadJwtToken(old.Session.Token).Claims.Single(x => x.Type == "shift_sv").Value);
            Assert.True(old.Session.TokenLifeTimeInSeconds <= 900);
            Assert.IsType<AuthenticationRefused>(await setup.RefreshAsync(old.Session.RefreshToken));
        }
        await State(2, NewPassword);
    }

    [Fact]
    public async Task Concurrent_password_operations_cannot_overwrite_each_other_or_issue_twice()
    {
        await fixture.ResetAsync(); using var setup = new IdentityHttpHost(fixture);
        var a = await Prepare(setup); var b = await Prepare(setup);
        using var gate = new AdmissionGate("PasswordMutation");
        using var first = new IdentityHttpHost(fixture, observe: gate.Observe);
        var signal = new SqlCommandSignal("UPDLOCK");
        using var second = new IdentityHttpHost(fixture, null, null, signal);
        var firstChange = first.ChangePasswordAsync(a.Handle, NewPassword, a.Verifier);
        Task<AuthOutcome>? secondChange = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            secondChange = second.ChangePasswordAsync(b.Handle, "Another synthetic phrase 97!", b.Verifier);
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10)); Assert.False(secondChange.IsCompleted);
        }
        finally { gate.Release(); }
        Assert.IsType<PasswordChanged>(await firstChange);
        Assert.IsType<AuthenticationRefused>(await secondChange!);
        await State(2, NewPassword);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "PasswordChanged"));
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("factor")]
    [InlineData("hash")]
    [InlineData("expiry")]
    [InlineData("cancel")]
    public async Task Prepared_password_cannot_be_admitted_after_its_context_changes(string change)
    {
        var clock = new ControlledClock(DateTimeOffset.UtcNow); fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync(); using var setup = new IdentityHttpHost(fixture);
            var ready = await Prepare(setup);
            using var gate = new AdmissionGate("PasswordPrepared");
            using var host = new IdentityHttpHost(fixture, observe: gate.Observe);
            var pending = host.ChangePasswordAsync(ready.Handle, NewPassword, ready.Verifier);
            try
            {
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                await using var db = fixture.CreateContext();
                if (change == "version") await db.Set<UserSecurityState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 2));
                if (change == "policy") await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.Revision, 2));
                if (change == "factor") await db.Set<UserSecurityState>().ExecuteUpdateAsync(x => x.SetProperty(s => s.FactorGeneration, 2));
                if (change == "hash")
                {
                    var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
                    var upgrade = HashService.GenerateVersionedHash(fixture.Password);
                    user.PasswordHash = upgrade.PasswordHash; user.Salt = upgrade.Salt; await db.SaveChangesAsync();
                }
                if (change == "expiry") clock.Advance(TimeSpan.FromMinutes(5));
                if (change == "cancel") Assert.IsType<OperationCancelled>(await setup.CancelAsync(ready.Handle, ready.Verifier));
            }
            finally { gate.Release(); }
            Assert.IsType<AuthenticationRefused>(await pending);
            await State(change == "version" ? 2 : 1, fixture.Password);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_failure_rolls_back_password_version_operation_MFA_and_issuance(bool afterSave)
    {
        fixture.Clock = new ControlledClock(DateTimeOffset.UtcNow);
        try
        {
            await fixture.ResetAsync(true); using var setup = new IdentityHttpHost(fixture);
            var ready = await PrepareRequiredMfa(setup);
            using var host = new IdentityHttpHost(fixture, null, null, new CompletionSaveFault(afterSave));
            var code = new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
            Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await host.PasswordMfaAsync(ready.Handle, code, ready.Verifier)).Code);
            await State(1, fixture.Password);
            await using (var db = fixture.CreateContext())
            {
                var op = await db.Set<AuthenticationOperation>().SingleAsync();
                Assert.Equal(AuthenticationOperationState.AwaitingMfa, op.State); Assert.NotNull(op.PendingPasswordHash);
                Assert.Null((await db.Set<UserSecurityState>().SingleAsync()).LastAcceptedTotpStep);
                Assert.Empty(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "PasswordChanged" || x.Outcome == "SessionIssued").ToListAsync());
            }
            Assert.IsType<PasswordChanged>(await setup.PasswordMfaAsync(ready.Handle, code, ready.Verifier));
            await State(2, NewPassword);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Lost_final_commit_response_cannot_replay_mutation_or_recover_an_extra_session()
    {
        await fixture.ResetAsync(); using var setup = new IdentityHttpHost(fixture); var ready = await Prepare(setup);
        using var host = new IdentityHttpHost(fixture, null, null, new LostPasswordCommitResponse());
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(await host.ChangePasswordAsync(ready.Handle, NewPassword, ready.Verifier)).Code);
        await State(2, NewPassword);
        Assert.IsType<AuthenticationRefused>(await setup.ChangePasswordAsync(ready.Handle, NewPassword, ready.Verifier));
        Assert.IsType<AuthenticationRefused>(await setup.RefreshAsync(ready.Session.Session.RefreshToken));
        Assert.IsType<SessionIssued>(await setup.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, NewPassword));
    }

    [Fact]
    public async Task Cancellation_before_commit_rolls_back_the_entire_password_mutation()
    {
        await fixture.ResetAsync(); using var setup = new IdentityHttpHost(fixture); var ready = await Prepare(setup);
        using var cancellation = new CancellationTokenSource();
        using var host = new IdentityHttpHost(fixture, observe: point => { if (point == "PasswordMutation") cancellation.Cancel(); });
        using var scope = host.Services.CreateScope();
        var services = scope.ServiceProvider.GetRequiredService<IdentityAdmissionServices>();
        var result = await AccountSecurityService.CompletePasswordChangeAsync(services, ready.Handle, new(NewPassword, ready.Verifier), cancellation.Token);
        Assert.Equal(AuthenticationFailure.Unavailable, Assert.IsType<AuthenticationRefused>(result).Code);
        await State(1, fixture.Password);
        Assert.IsType<PasswordChanged>(await setup.ChangePasswordAsync(ready.Handle, NewPassword, ready.Verifier));
    }

    private async Task<(SessionIssued Session, string Handle, string Verifier)> Prepare(IdentityHttpHost host)
    {
        var session = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        var pkce = IdentityHttpHost.Pkce();
        var start = Assert.IsType<ChallengeRequired>(await host.StartPasswordChangeAsync(session.Session.Token, pkce.Challenge));
        var ready = Assert.IsType<ChallengeRequired>(await host.ProvePasswordAsync(start.Challenge.Handle!, fixture.Password, pkce.Verifier));
        return (session, ready.Challenge.Handle!, pkce.Verifier);
    }
    private async Task<(string Handle, string Verifier)> PrepareRequiredMfa(IdentityHttpHost host)
    {
        await using var db = fixture.CreateContext();
        await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.RequireChangePassword, true));
        var pkce = IdentityHttpHost.Pkce();
        var start = Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, pkce.Challenge));
        var pending = Assert.IsType<ChallengeRequired>(await host.ChangePasswordAsync(start.Challenge.Handle!, NewPassword, pkce.Verifier));
        return (pending.Challenge.Handle!, pkce.Verifier);
    }
    private async Task State(long version, string password)
    {
        await using var db = fixture.CreateContext(); var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.Equal(version, (await db.Set<UserSecurityState>().SingleAsync()).SecurityVersion);
        Assert.True(HashService.VerifyVersionedPassword(password, user.Salt, user.PasswordHash));
    }
    private sealed class LostPasswordCommitResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuthenticationOperation>().Any(x => x.Entity.State == AuthenticationOperationState.Completed))
                throw new IOException("Synthetic lost final commit response.");
            return Task.CompletedTask;
        }
    }
}
