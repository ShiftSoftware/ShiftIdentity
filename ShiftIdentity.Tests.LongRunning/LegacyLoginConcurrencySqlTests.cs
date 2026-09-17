using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;
using static ShiftIdentity.Tests.LegacyLoginCompatibilitySqlTests;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class LegacyLoginConcurrencySqlTests(SqlIdentityFixture fixture)
{
    private LegacyLoginCompatibilitySqlTests Flow => new(fixture);

    [Theory]
    [InlineData("version")]
    [InlineData("password")]
    [InlineData("salt")]
    [InlineData("factor")]
    [InlineData("policy")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("locked")]
    [InlineData("recovery")]
    [InlineData("email")]
    public async Task Mutation_after_password_proof_wins_before_admission(string change)
    {
        await Flow.Prepare();
        using var gate = new AdmissionGate("LegacyLoginProof");
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var login = Flow.Login(host.Client);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var security = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
            switch (change)
            {
                case "version": security.SecurityVersion++; break;
                case "factor": security.FactorGeneration++; break;
                case "password": user.PasswordHash = HashService.GenerateHash("Different Synthetic Password 8!").PasswordHash; break;
                case "salt": user.Salt = HashService.GenerateHash("Different Synthetic Password 8!").Salt; break;
                case "policy": policy.Revision++; break;
                case "inactive": user.IsActive = false; break;
                case "deleted": user.IsDeleted = true; break;
                case "locked": user.LockDownUntil = DateTime.UtcNow.AddMinutes(5); break;
                case "recovery": security.LocalMfaRecoveryRequired = true; break;
                case "email": user.Email = "synthetic@example.invalid"; policy.RequireVerifiedEmail = true; break;
            }
            await db.SaveChangesAsync();
        }
        finally { gate.Release(); }
        Assert.Equal(HttpStatusCode.BadRequest, (await login).StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued"));
    }

    [Fact]
    public async Task Login_admitted_first_keeps_its_version_and_fixed_expiry_while_revocation_waits()
    {
        await Flow.Prepare();
        using var gate = new AdmissionGate("AdmissionLock");
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var login = Flow.Login(host.Client);
        var signal = new SqlCommandSignal("UPDATE");
        await using var writer = fixture.CreateContext(signal);
        Task<int>? mutation = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            mutation = writer.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, y => y.SecurityVersion + 1));
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(mutation.IsCompleted);
        }
        finally { gate.Release(); }
        var session = await Entity<TokenDTO>(await login);
        await mutation!;
        Assert.Equal("1", Jwt(session.Token)["shift_sv"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { session.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Proof_expiry_is_rechecked_after_waiting_for_admission()
    {
        await Flow.Prepare();
        var clock = new ControlledClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        fixture.Clock = clock;
        try
        {
            using var gate = new AdmissionGate("LegacyLoginProof");
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
            var login = Flow.Login(host.Client);
            try { await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10)); clock.Advance(TimeSpan.FromMinutes(5)); }
            finally { gate.Release(); }
            Assert.Equal(HttpStatusCode.BadRequest, (await login).StatusCode);
            await using var db = fixture.CreateContext();
            Assert.Equal(DateTimeOffset.UnixEpoch, (await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID)).UserLog!.LastSeen);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_Mfa_completions_of_one_or_two_steps_accept_one_factor_use(bool separateOperations)
    {
        await Flow.Prepare(true);
        using var first = Flow.Host(); using var second = Flow.Host();
        var a = await Entity<TokenDTO>(await Flow.Login(first.Client));
        var b = separateOperations ? await Entity<TokenDTO>(await Flow.Login(second.Client)) : a;
        var responses = await Task.WhenAll(Flow.Mfa(first.Client, a.Token), Flow.Mfa(second.Client, b.Token));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "MfaCompleted").ToListAsync());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Login_save_failure_rolls_back_hash_attempts_LastSeen_operation_and_audit(bool afterSave, bool mfa)
    {
        await Flow.Prepare(mfa);
        byte[] hash;
        await using (var db = fixture.CreateContext()) hash = (await db.Users.SingleAsync(x => x.ID == fixture.UserID)).PasswordHash;
        using var failing = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, new LoginSaveFault(afterSave));
        await RefusedWithoutToken(await Flow.Login(failing.Client));
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
            Assert.Equal(hash, user.PasswordHash); Assert.Equal(DateTimeOffset.UnixEpoch, user.UserLog!.LastSeen);
            Assert.Empty(await db.Set<AuthenticationOperation>().ToListAsync());
            Assert.Empty(await db.Set<AuthenticationAuditEvent>().ToListAsync());
        }
        using var healthy = Flow.Host();
        await Entity<TokenDTO>(await Flow.Login(healthy.Client));
    }

    [Fact]
    public async Task Credentials_do_not_escape_while_commit_is_waiting()
    {
        await Flow.Prepare();
        var barrier = new LoginCommitBarrier();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, barrier);
        var login = Flow.Login(host.Client);
        try
        {
            await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(login.IsCompleted);
        }
        finally { barrier.Release.TrySetResult(); }
        await Entity<TokenDTO>(await login);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_login_commit_returns_no_credentials_and_never_retries_itself(bool mfa)
    {
        await Flow.Prepare(mfa);
        using var uncertain = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, new LoginCommitFault());
        await RefusedWithoutToken(await Flow.Login(uncertain.Client));
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyLoginPasswordProven").ToListAsync());
        Assert.Equal(mfa ? 1 : 0, await db.Set<AuthenticationOperation>().CountAsync());
        // The old request has no idempotency ID. A later fresh password proof may start a new login; no lost pair is recovered.
        using var healthy = Flow.Host();
        await Entity<TokenDTO>(await Flow.Login(healthy.Client));
    }

    [Fact]
    public async Task Lost_Mfa_commit_refuses_replay_without_reissuing_credentials()
    {
        await Flow.Prepare(true);
        using var first = Flow.Host();
        var step = await Entity<TokenDTO>(await Flow.Login(first.Client));
        using var uncertain = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, new LostCommitResponse());
        await RefusedWithoutToken(await Flow.Mfa(uncertain.Client, step.Token));
        Assert.Equal(HttpStatusCode.BadRequest, (await Flow.Mfa(first.Client, step.Token)).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(AuthenticationOperationState.Completed, (await db.Set<AuthenticationOperation>().SingleAsync()).State);
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "SessionIssued").ToListAsync());
    }

    private static async Task RefusedWithoutToken(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        }
    }
    private static bool LoginWritten(DbContext? db) => db?.ChangeTracker.Entries<AuthenticationAuditEvent>()
        .Any(x => x.Entity.Outcome == "LegacyLoginPasswordProven") == true;
    private sealed class LoginSaveFault(bool afterSave) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (!afterSave && LoginWritten(e.Context)) throw new IOException("Synthetic login save failure.");
            return ValueTask.FromResult(result);
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e, int result, CancellationToken ct = default)
        {
            if (afterSave && LoginWritten(e.Context)) throw new IOException("Synthetic login flush failure.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class LoginCommitFault : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData e, CancellationToken ct = default)
        {
            if (LoginWritten(e.Context)) throw new IOException("Synthetic lost login commit response.");
            return Task.CompletedTask;
        }
    }
    private sealed class LoginCommitBarrier : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData e,
            InterceptionResult result, CancellationToken ct = default)
        {
            if (LoginWritten(e.Context)) { Entered.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), ct); }
            return result;
        }
    }
}
