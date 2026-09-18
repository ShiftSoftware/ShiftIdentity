using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;
using static ShiftIdentity.Tests.LegacyLoginCompatibilitySqlTests;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class LegacyMfaTokenConcurrencySqlTests(SqlIdentityFixture fixture)
{
    private LegacyMfaTokenCompatibilitySqlTests Flow => new(fixture);

    [Fact]
    public async Task Competing_hosts_exchange_one_credential_once()
    {
        var step = await Flow.PrepareAsync();
        using var first = Flow.Staged(); using var second = Flow.Staged();
        var code = Flow.Code();
        var responses = await Task.WhenAll(Flow.Mfa(first.Client, step.Token, code), Flow.Mfa(second.Client, step.Token, code));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange).ToListAsync());
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyMfaExchanged").ToListAsync());
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "SessionIssued").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_before_or_after_flush_rolls_back_the_exchange_and_allows_retry(bool afterSave)
    {
        var step = await Flow.PrepareAsync();
        using var failing = Flow.Staged(null, new CompletionSaveFault(afterSave));
        using var refused = await Flow.Mfa(failing.Client, step.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);
        Assert.Null((await refused.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange));
            Assert.False(await db.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "LegacyMfaExchanged"));
            Assert.Null((await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).LastAcceptedTotpStep);
        }
        using var healthy = Flow.Staged();
        await Entity<TokenDTO>(await Flow.Mfa(healthy.Client, step.Token));
    }

    [Fact]
    public async Task Lost_commit_response_returns_no_credentials_and_the_tombstone_blocks_replay()
    {
        var step = await Flow.PrepareAsync();
        using var uncertain = Flow.Staged(null, new LostCommitResponse());
        using var response = await Flow.Mfa(uncertain.Client, step.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        using var healthy = Flow.Staged();
        Assert.Equal(HttpStatusCode.BadRequest, (await Flow.Mfa(healthy.Client, step.Token)).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(AuthenticationOperationState.Completed, (await db.Set<AuthenticationOperation>()
            .SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange)).State);
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "SessionIssued").ToListAsync());
    }

    [Fact]
    public async Task Deadline_is_rechecked_after_waiting_for_admission()
    {
        var step = await Flow.PrepareAsync();
        var clock = new ControlledClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        fixture.Clock = clock;
        try
        {
            using var gate = new AdmissionGate("LegacyMfaProof");
            using var host = Flow.Staged(gate.Observe);
            var exchange = Flow.Mfa(host.Client, step.Token, Flow.Code(clock.GetUtcNow()));
            try { await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10)); clock.Advance(TimeSpan.FromMinutes(5)); }
            finally { gate.Release(); }
            Assert.Equal(HttpStatusCode.BadRequest, (await exchange).StatusCode);
            await using var db = fixture.CreateContext();
            Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange));
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Revocation_between_proof_and_admission_is_the_single_allowed_current_state_stamp()
    {
        var step = await Flow.PrepareAsync();
        using var gate = new AdmissionGate("LegacyMfaProof");
        using var host = Flow.Staged(gate.Observe);
        var exchange = Flow.Mfa(host.Client, step.Token);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = fixture.CreateContext();
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, y => y.SecurityVersion + 1));
        }
        finally { gate.Release(); }
        var session = await Entity<TokenDTO>(await exchange);
        Assert.Equal("2", Jwt(session.Token)["shift_sv"]);
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, (await verify.Set<AuthenticationOperation>().SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange)).SecurityVersion);
    }

    [Fact]
    public async Task Admission_first_fixes_the_stamped_version_before_a_waiting_revocation()
    {
        var step = await Flow.PrepareAsync();
        using var gate = new AdmissionGate("AdmissionLock");
        using var host = Flow.Staged(gate.Observe);
        var exchange = Flow.Mfa(host.Client, step.Token);
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
        var session = await Entity<TokenDTO>(await exchange);
        await mutation!;
        Assert.Equal("1", Jwt(session.Token)["shift_sv"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { session.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Flow.Mfa(host.Client, step.Token)).StatusCode);
    }
}
