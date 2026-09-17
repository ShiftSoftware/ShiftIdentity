using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class LegacyRefreshConcurrencySqlTests(SqlIdentityFixture fixture)
{
    [Fact]
    public async Task Competing_hosts_exchange_one_credential_once()
    {
        var token = await PrepareAsync();
        using var first = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        using var second = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var responses = await Task.WhenAll(RefreshAsync(first.Client, token), RefreshAsync(second.Client, token));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<AuthenticationOperation>().Where(x =>
            x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange).ToListAsync());
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyRefreshExchanged").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_before_or_after_flush_rolls_back_exchange_and_allows_retry(bool afterSave)
    {
        var token = await PrepareAsync();
        using var failing = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null,
            new CompletionSaveFault(afterSave));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await RefreshAsync(failing.Client, token)).StatusCode);
        await using (var db = fixture.CreateContext())
        {
            Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x =>
                x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange));
            Assert.False(await db.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "LegacyRefreshExchanged"));
        }
        using var healthy = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(healthy.Client, token)).StatusCode);
    }

    [Fact]
    public async Task Lost_commit_response_returns_no_credentials_and_tombstone_blocks_replay()
    {
        var token = await PrepareAsync();
        using var uncertain = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, new LostCommitResponse());
        using var response = await RefreshAsync(uncertain.Client, token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        using var healthy = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(healthy.Client, token)).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<AuthenticationOperation>().Where(x =>
            x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange).ToListAsync());
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyRefreshExchanged").ToListAsync());
    }

    [Fact]
    public async Task Deadline_is_rechecked_after_waiting_for_admission()
    {
        var clock = Clock(); fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync();
            var token = LegacyToken(fixture.UserID.ToString(), clock.GetUtcNow().AddMinutes(5));
            using var gate = new AdmissionGate("LegacyRefreshProof");
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
            var refresh = RefreshAsync(host.Client, token);
            try
            {
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                clock.Advance(TimeSpan.FromMinutes(5));
            }
            finally { gate.Release(); }
            Assert.Equal(HttpStatusCode.BadRequest, (await refresh).StatusCode);
            await using var db = fixture.CreateContext();
            Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x =>
                x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange));
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Admission_first_fixes_the_stamped_version_before_a_waiting_revocation()
    {
        var token = await PrepareAsync();
        using var gate = new AdmissionGate("AdmissionLock");
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var exchange = RefreshAsync(host.Client, token);
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
        var exchanged = await EntityAsync<TokenDTO>(await exchange);
        await mutation!;
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(exchanged.RefreshToken).Claims,
            x => x.Type == "shift_sv" && x.Value == "1");
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(host.Client, exchanged.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(host.Client, token)).StatusCode);
    }

    [Fact]
    public async Task Revocation_after_legacy_proof_is_the_single_allowed_current_state_stamp()
    {
        var token = await PrepareAsync();
        using var gate = new AdmissionGate("LegacyRefreshProof");
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var exchange = RefreshAsync(host.Client, token);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = fixture.CreateContext();
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, y => y.SecurityVersion + 1));
        }
        finally { gate.Release(); }
        var exchanged = await EntityAsync<TokenDTO>(await exchange);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(exchanged.RefreshToken).Claims,
            x => x.Type == "shift_sv" && x.Value == "2");
        await using var verify = fixture.CreateContext();
        Assert.Equal(2, (await verify.Set<AuthenticationOperation>().SingleAsync(x =>
            x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange)).SecurityVersion);
    }

    private async Task<string> PrepareAsync()
    {
        fixture.Clock = TimeProvider.System;
        fixture.LegacyRefreshLifetimeSeconds = 1800;
        await fixture.ResetAsync();
        using var issuer = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        var login = await EntityAsync<TokenDTO>(await issuer.Client.PostAsJsonAsync("/api/Auth/Login",
            new LoginDTO { Username = fixture.Username, Password = fixture.Password }));
        return login.RefreshToken;
    }

    private string LegacyToken(string subject, DateTimeOffset expires)
    {
        var jwt = new JwtSecurityToken("https://legacy.invalid", "legacy-refresh",
            [new Claim(ClaimTypes.NameIdentifier, subject)], expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(fixture.LegacyRefreshKey)), SecurityAlgorithms.HmacSha512Signature));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refresh) =>
        client.PostAsJsonAsync("/api/Auth/Refresh", new { RefreshToken = refresh });

    private static async Task<T> EntityAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<T>>())!.Entity!;
        }
    }

    private static ControlledClock Clock() => new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
}
