using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
public sealed class AppCodeConcurrencySqlTests(SqlIdentityFixture fixture)
{
    private async Task<TokenDTO> PrepareAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            var app = await db.Apps.IgnoreQueryFilters().SingleAsync(x => x.AppId == "other-client");
            app.AppSecret = null; app.IsDeleted = false; app.RedirectUri = "https://other.invalid/callback";
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        return Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Session;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string access, string verifier)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/AuthCode");
        request.Headers.Authorization = new("Bearer", access);
        request.Content = JsonContent.Create(new GenerateAuthCodeDTO
            { AppId = "other-client", ReturnUrl = "/orders", CodeChallenge = HashService.SHA512GenerateHash(verifier) });
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient client, Guid code, string verifier) =>
        client.PostAsJsonAsync("/api/Auth/TokenWithAppIdOnly", new GenerateExternalTokenWithAppIdOnlyDTO
            { AppId = "other-client", AuthCode = code, CodeVerifier = verifier });

    private static async Task<T> Entity<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<T>>())!.Entity!;
        }
    }

    [Fact]
    public async Task Competing_hosts_consume_once_and_return_one_pair()
    {
        var session = await PrepareAsync();
        using var first = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        using var second = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(first.Client, session.Token, verifier));
        var responses = await Task.WhenAll(ExchangeAsync(first.Client, code.Code, verifier), ExchangeAsync(second.Client, code.Code, verifier));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.BadRequest);
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "AppCodeExchanged").ToListAsync());
        Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).SecurityVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_before_or_after_flush_rolls_back_consumption_and_allows_retry(bool afterSave)
    {
        var session = await PrepareAsync();
        using var initial = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(initial.Client, session.Token, verifier));
        using var failing = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, new CompletionSaveFault(afterSave));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ExchangeAsync(failing.Client, code.Code, verifier)).StatusCode);
        await using (var db = fixture.CreateContext())
        {
            Assert.Equal(AuthenticationOperationState.AwaitingAppExchange,
                (await db.Set<AuthenticationOperation>().SingleAsync(x => x.ID == code.Code)).State);
            Assert.False(await db.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "AppCodeExchanged"));
        }
        await Entity<TokenDTO>(await ExchangeAsync(initial.Client, code.Code, verifier));
    }

    [Fact]
    public async Task Lost_commit_response_returns_no_credentials_and_replay_cannot_reissue()
    {
        var session = await PrepareAsync();
        using var initial = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(initial.Client, session.Token, verifier));
        using var uncertain = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, new LostCommitResponse());
        using var response = await ExchangeAsync(uncertain.Client, code.Code, verifier);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(initial.Client, code.Code, verifier)).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Equal(AuthenticationOperationState.Completed, (await db.Set<AuthenticationOperation>().SingleAsync(x => x.ID == code.Code)).State);
        Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "AppCodeExchanged").ToListAsync());
    }

    [Theory]
    [InlineData("AppCodeProof")]
    [InlineData("AppExchangeProof")]
    public async Task A_version_change_after_reading_proof_cannot_be_promoted(string checkpoint)
    {
        var session = await PrepareAsync();
        var verifier = Guid.NewGuid().ToString();
        using var initial = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var code = await Entity<AuthCodeModel>(await CreateAsync(initial.Client, session.Token, verifier));
        using var gate = new AdmissionGate(checkpoint);
        using var blocked = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var request = checkpoint == "AppCodeProof" ? CreateAsync(blocked.Client, session.Token, verifier)
            : ExchangeAsync(blocked.Client, code.Code, verifier);
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await using var db = fixture.CreateContext();
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
        }
        finally { gate.Release(); }
        Assert.Equal(HttpStatusCode.BadRequest, (await request).StatusCode);
    }

    [Fact]
    public async Task Admission_before_mutation_fixes_old_version_before_returning_credentials()
    {
        var session = await PrepareAsync();
        var verifier = Guid.NewGuid().ToString();
        using var initial = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var code = await Entity<AuthCodeModel>(await CreateAsync(initial.Client, session.Token, verifier));
        using var gate = new AdmissionGate("AdmissionLock");
        using var blocked = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var exchange = ExchangeAsync(blocked.Client, code.Code, verifier);
        var signal = new SqlCommandSignal("UPDATE");
        await using var writer = fixture.CreateContext(signal);
        Task<int>? mutation = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            mutation = writer.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(mutation.IsCompleted);
        }
        finally { gate.Release(); }
        var token = await Entity<TokenDTO>(await exchange);
        await mutation!;
        var access = new JwtSecurityTokenHandler().ReadJwtToken(token.Token);
        Assert.Contains(access.Claims, x => x.Type == "shift_sv" && x.Value == "1");
        Assert.True(access.ValidTo <= DateTime.UtcNow.AddMinutes(15));
        Assert.Equal(HttpStatusCode.BadRequest, (await initial.Client.PostAsJsonAsync("/api/Auth/Refresh", new { token.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task App_mutation_waits_for_code_creation_and_invalidates_its_saved_binding()
    {
        var session = await PrepareAsync();
        var verifier = Guid.NewGuid().ToString();
        using var gate = new AdmissionGate("AdmissionLock");
        using var blocked = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
        var create = CreateAsync(blocked.Client, session.Token, verifier);
        var signal = new SqlCommandSignal("UPDATE");
        await using var writer = fixture.CreateContext(signal);
        Task<int>? mutation = null;
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            mutation = writer.Apps.Where(x => x.AppId == "other-client")
                .ExecuteUpdateAsync(x => x.SetProperty(s => s.RedirectUri, "https://changed.invalid/callback"));
            await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(mutation.IsCompleted);
        }
        finally { gate.Release(); }
        var code = await Entity<AuthCodeModel>(await create);
        await mutation!;
        Assert.Equal("https://other.invalid/callback", code.RedirectUri);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(blocked.Client, code.Code, verifier)).StatusCode);
    }

    [Fact]
    public async Task Mutation_holding_the_user_lock_is_seen_by_the_waiting_exchange()
    {
        var session = await PrepareAsync();
        var verifier = Guid.NewGuid().ToString();
        using var initial = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var code = await Entity<AuthCodeModel>(await CreateAsync(initial.Client, session.Token, verifier));
        await using var writer = fixture.CreateContext();
        await using var transaction = await writer.Database.BeginTransactionAsync();
        await writer.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, s => s.SecurityVersion + 1));
        var signal = new SqlCommandSignal("UPDLOCK");
        using var waiting = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, null, signal);
        var exchange = ExchangeAsync(waiting.Client, code.Code, verifier);
        await signal.Entered.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(exchange.IsCompleted);
        await transaction.CommitAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await exchange).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deadline_is_rechecked_after_waiting_for_admission(bool creation)
    {
        var session = await PrepareAsync();
        var verifier = Guid.NewGuid().ToString();
        var clock = new ControlledClock(DateTimeOffset.UtcNow);
        fixture.Clock = clock;
        try
        {
            using var initial = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var code = await Entity<AuthCodeModel>(await CreateAsync(initial.Client, session.Token, verifier));
            using var gate = new AdmissionGate(creation ? "AppCodeProof" : "AppExchangeProof");
            using var blocked = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, true, gate.Observe);
            var request = creation ? CreateAsync(blocked.Client, session.Token, verifier) : ExchangeAsync(blocked.Client, code.Code, verifier);
            try
            {
                await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
                clock.Advance(TimeSpan.FromMinutes(creation ? 16 : 5));
            }
            finally { gate.Release(); }
            Assert.Equal(HttpStatusCode.BadRequest, (await request).StatusCode);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }
}
