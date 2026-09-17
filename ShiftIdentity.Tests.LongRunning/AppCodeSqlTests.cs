using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
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
public sealed class AppCodeSqlTests(SqlIdentityFixture fixture)
{
    private const string Target = "other-client";
    private const string ReturnUrl = "/orders?view=recent#selected";

    private async Task<TokenDTO> PrepareAsync(bool mfa = false)
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync(mfa);
        await using (var db = fixture.CreateContext())
        {
            var apps = await db.Apps.IgnoreQueryFilters().ToListAsync();
            foreach (var app in apps)
            {
                app.IsDeleted = false; app.AppSecret = null;
                app.RedirectUri = app.AppId == Target ? "https://other.invalid/callback" : "https://client.invalid/callback";
            }
            await db.SaveChangesAsync();
        }
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var outcome = await host.LoginAsync(fixture, pkce.Challenge);
        if (outcome is ChallengeRequired challenge)
            outcome = await host.CompleteAsync(challenge.Challenge.Handle!,
                new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime), pkce.Verifier);
        return Assert.IsType<SessionIssued>(outcome).Session;
    }

    private static Task<HttpResponseMessage> CreateAsync(HttpClient client, string access, string verifier,
        string appId = Target, string? returnUrl = ReturnUrl, string? challenge = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/AuthCode");
        request.Headers.Authorization = new("Bearer", access);
        request.Content = JsonContent.Create(new GenerateAuthCodeDTO
            { AppId = appId, ReturnUrl = returnUrl, CodeChallenge = challenge ?? HashService.SHA512GenerateHash(verifier) });
        return client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> ExchangeAsync(HttpClient client, Guid code, string verifier, string appId = Target) =>
        client.PostAsJsonAsync("/api/Auth/TokenWithAppIdOnly", new GenerateExternalTokenWithAppIdOnlyDTO
            { AppId = appId, AuthCode = code, CodeVerifier = verifier });

    private static async Task<T> Entity<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<T>>())!.Entity!;
        }
    }

    private async Task<AuthenticationOperation> ReadAsync(Guid id)
    {
        await using var db = fixture.CreateContext();
        return await db.Set<AuthenticationOperation>().SingleAsync(x => x.ID == id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Deployed_contract_survives_host_restart_and_preserves_bound_proof_through_refresh(bool mfa)
    {
        var session = await PrepareAsync(mfa);
        var verifier = Guid.NewGuid().ToString();
        AuthCodeModel code;
        using (var creator = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true))
        {
            using var response = await CreateAsync(creator.Client, session.Token, verifier,
                challenge: HashService.SHA512GenerateHash(verifier).ToLowerInvariant());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
            var json = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(verifier, json);
            Assert.DoesNotContain("codeChallenge", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("userID", json, StringComparison.OrdinalIgnoreCase);
            code = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<AuthCodeModel>>())!.Entity!;
            Assert.Equal(ReturnUrl, code.ReturnUrl);
            Assert.Equal("https://other.invalid/callback", code.RedirectUri);
            Assert.Equal(Target, code.AppId);
        }
        var stored = await ReadAsync(code.Code);
        Assert.Equal(AuthenticationOperationPurpose.AppExchange, stored.Purpose);
        Assert.Equal(TimeSpan.FromMinutes(5), stored.ExpiresAt - stored.CreatedAt);
        Assert.Equal(fixture.UserID, stored.UserID);
        Assert.Equal(1, stored.SecurityVersion);
        Assert.Equal(1, stored.PolicyRevision);
        Assert.Equal(1, stored.FactorGeneration);
        Assert.Equal(Target, stored.ClientID);
        Assert.Equal(Target, stored.Audience);
        Assert.True(stored.External);
        Assert.Equal(mfa, stored.SessionMfaSatisfied);
        Assert.Equal(128, stored.CodeChallenge.Length);
        Assert.Empty(stored.HandleDigest);

        using var exchanger = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var exchanged = await Entity<TokenDTO>(await ExchangeAsync(exchanger.Client, code.Code, verifier));
        var access = new JwtSecurityTokenHandler().ReadJwtToken(exchanged.Token);
        var original = new JwtSecurityTokenHandler().ReadJwtToken(session.Token);
        Assert.Equal(Target, Assert.Single(access.Audiences));
        foreach (var token in new[] { exchanged.Token, exchanged.RefreshToken })
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            Assert.Equal("true", jwt.Claims.Single(x => x.Type == "shift_external").Value);
            Assert.Equal(Target, jwt.Claims.Single(x => x.Type == "shift_client").Value);
            Assert.Equal(Target, jwt.Claims.Single(x => x.Type == "shift_resource").Value);
            Assert.Equal(stored.AppBinding, jwt.Claims.Single(x => x.Type == "shift_app").Value);
            foreach (var claim in new[] { "shift_uid", "shift_sv", "shift_policy", "shift_factor", "shift_mfa", "auth_time", "sub" })
                Assert.Equal(original.Claims.Single(x => x.Type == claim).Value, jwt.Claims.Single(x => x.Type == claim).Value);
        }
        Assert.Contains(access.Claims, x => x.Type == ShiftIdentityClaims.ExternalToken && x.Value == "true");
        var renewed = await Entity<TokenDTO>(await exchanger.Client.PostAsJsonAsync("/api/Auth/Refresh", new { exchanged.RefreshToken }));
        var renewedClaims = new JwtSecurityTokenHandler().ReadJwtToken(renewed.RefreshToken).Claims.ToList();
        Assert.Contains(renewedClaims, x => x.Type == "shift_client" && x.Value == Target);
        Assert.Contains(renewedClaims, x => x.Type == "shift_app" && x.Value == stored.AppBinding);
        // Refresh is still reusable; it cannot become an internal session via the v2 route.
        Assert.Equal(HttpStatusCode.OK, (await exchanger.Client.PostAsJsonAsync("/api/Auth/Refresh", new { exchanged.RefreshToken })).StatusCode);
        using var internalHost = new IdentityHttpHost(fixture);
        Assert.IsType<AuthenticationRefused>(await internalHost.RefreshAsync(exchanged.RefreshToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(exchanger.Client, code.Code, verifier)).StatusCode);
        var completed = await ReadAsync(code.Code);
        Assert.Equal(AuthenticationOperationState.Completed, completed.State);
        Assert.Empty(completed.CodeChallenge);
        Assert.Null(completed.AppBinding);
        Assert.Null(completed.SessionAuthenticatedAt);
        await using var final = fixture.CreateContext();
        Assert.Single(await final.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "AppCodeExchanged").ToListAsync());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("factor")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("password")]
    [InlineData("recovery")]
    [InlineData("email")]
    [InlineData("enrollment")]
    [InlineData("app-deleted")]
    [InlineData("app-secret")]
    [InlineData("redirect")]
    public async Task Current_state_refuses_exchange_without_consuming_or_issuing(string change)
    {
        var session = await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
        await ChangeAsync(change);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(host.Client, code.Code, verifier)).StatusCode);
        Assert.Equal(AuthenticationOperationState.AwaitingAppExchange, (await ReadAsync(code.Code)).State);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "AppCodeExchanged"));
    }

    private async Task ChangeAsync(string change)
    {
        await using var db = fixture.CreateContext();
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
        var app = await db.Apps.SingleAsync(x => x.AppId == Target);
        switch (change)
        {
            case "version": state.SecurityVersion++; break;
            case "policy": policy.Revision++; break;
            case "factor": state.FactorGeneration++; break;
            case "inactive": user.IsActive = false; break;
            case "deleted": user.IsDeleted = true; break;
            case "password": user.RequireChangePassword = true; break;
            case "recovery": state.LocalMfaRecoveryRequired = true; break;
            case "email": user.Email = "synthetic@example.invalid"; policy.RequireVerifiedEmail = true; break;
            case "enrollment": policy.MfaMandatory = true; break;
            case "app-deleted": app.IsDeleted = true; break;
            case "app-secret": app.AppSecret = "synthetic-secret"; break;
            case "redirect": app.RedirectUri = "https://changed.invalid/callback"; break;
        }
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("version")]
    [InlineData("policy")]
    [InlineData("factor")]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("password")]
    [InlineData("recovery")]
    [InlineData("email")]
    [InlineData("app-deleted")]
    [InlineData("app-secret")]
    [InlineData("redirect")]
    public async Task Compatible_refresh_rechecks_app_and_security_state(string change)
    {
        var session = await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
        var token = await Entity<TokenDTO>(await ExchangeAsync(host.Client, code.Code, verifier));
        await ChangeAsync(change);
        var response = await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { token.RefreshToken });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
    }

    [Theory]
    [InlineData("version")]
    [InlineData("factor")]
    [InlineData("password")]
    [InlineData("recovery")]
    [InlineData("app-secret")]
    public async Task Code_creation_checks_current_state_before_storing(string change)
    {
        var session = await PrepareAsync();
        await ChangeAsync(change);
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(host.Client, session.Token, Guid.NewGuid().ToString())).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.False(await db.Set<AuthenticationOperation>().AnyAsync());
    }

    [Theory]
    [InlineData("https://outside.invalid")]
    [InlineData("//outside.invalid")]
    [InlineData("\\outside.invalid")]
    [InlineData("/orders\r\nLocation: https://outside.invalid")]
    public async Task Absolute_or_unsafe_return_urls_are_refused(string returnUrl)
    {
        var session = await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(host.Client, session.Token,
            Guid.NewGuid().ToString(), returnUrl: returnUrl)).StatusCode);
    }

    [Fact]
    public async Task Wrong_app_cannot_consume_and_wrong_verifiers_share_a_five_attempt_limit_across_hosts()
    {
        var session = await PrepareAsync();
        using var one = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        using var two = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(one.Client, session.Token, verifier));
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(two.Client, code.Code, verifier, "test-client")).StatusCode);
        Assert.Equal(0, (await ReadAsync(code.Code)).FailedAttempts);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(i % 2 == 0 ? one.Client : two.Client,
                code.Code, Guid.NewGuid().ToString())).StatusCode);
        var locked = await ReadAsync(code.Code);
        Assert.Equal(5, locked.FailedAttempts);
        Assert.Equal(AuthenticationOperationState.Locked, locked.State);
        Assert.Empty(locked.CodeChallenge);
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(two.Client, code.Code, verifier)).StatusCode);
    }

    [Fact]
    public async Task Single_wrong_verifier_does_not_consume_a_valid_code()
    {
        var session = await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(host.Client, code.Code, Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ExchangeAsync(host.Client, code.Code, verifier)).StatusCode);
    }

    [Fact]
    public async Task Codes_have_five_minutes_without_borrowing_a_sensitive_proof_deadline()
    {
        var session = await PrepareAsync();
        var clock = new ControlledClock(DateTimeOffset.UtcNow.AddMinutes(6));
        fixture.Clock = clock;
        try
        {
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var verifier = Guid.NewGuid().ToString();
            var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
            clock.Advance(TimeSpan.FromSeconds(299));
            var token = await Entity<TokenDTO>(await ExchangeAsync(host.Client, code.Code, verifier));
            var original = new JwtSecurityTokenHandler().ReadJwtToken(session.Token);
            Assert.Equal(original.Claims.Single(x => x.Type == "auth_time").Value,
                new JwtSecurityTokenHandler().ReadJwtToken(token.Token).Claims.Single(x => x.Type == "auth_time").Value);
            var expired = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
            clock.Advance(TimeSpan.FromMinutes(5));
            Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(host.Client, expired.Code, verifier)).StatusCode);
            await using var db = fixture.CreateContext();
            Assert.Equal(1, await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow()));
            var cleared = await ReadAsync(expired.Code);
            Assert.Equal(AuthenticationOperationState.Cancelled, cleared.State);
            Assert.Empty(cleared.CodeChallenge);
            Assert.Null(cleared.AppBinding);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task External_access_and_refresh_credentials_cannot_create_internal_app_codes()
    {
        var session = await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
        var token = await Entity<TokenDTO>(await ExchangeAsync(host.Client, code.Code, verifier));
        foreach (var credential in new[] { token.Token, token.RefreshToken, session.RefreshToken })
            Assert.Equal(HttpStatusCode.Unauthorized, (await CreateAsync(host.Client, credential, verifier)).StatusCode);
    }

    [Fact]
    public async Task Staged_services_do_not_expose_the_process_store_or_bare_user_id_bypass()
    {
        await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        using var scope = host.Services.CreateScope();
        var codes = scope.ServiceProvider.GetRequiredService<AuthCodeService>();
        var verifier = Guid.NewGuid().ToString();
        var code = new AuthCodeModel { Code = Guid.NewGuid(), UserID = fixture.UserID, AppId = Target,
            CodeChallenge = HashService.SHA512GenerateHash(verifier), Expire = DateTime.UtcNow.AddMinutes(5) };
        scope.ServiceProvider.GetRequiredService<AuthCodeStoreService>().AddCode(code);
        Assert.Null(await codes.GenerateCodeAsync(new() { AppId = Target, CodeChallenge = code.CodeChallenge }, fixture.UserID));
        Assert.Null(await codes.VerifyCodeByAppIdOnly(Target, code.Code, verifier));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<AuthService>().GenrerateExternalTokenWithAppIdOnly(
            new() { AppId = Target, AuthCode = code.Code, CodeVerifier = verifier }));
    }

    [Fact]
    public async Task Production_registration_keeps_legacy_app_code_and_refresh_shapes()
    {
        await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        var session = await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Login",
            new LoginDTO { Username = fixture.Username, Password = fixture.Password }));
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
        var token = await Entity<TokenDTO>(await ExchangeAsync(host.Client, code.Code, verifier));
        Assert.DoesNotContain(new JwtSecurityTokenHandler().ReadJwtToken(token.Token).Claims, x => x.Type == "shift_schema");
        await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { token.RefreshToken }));
        await using var db = fixture.CreateContext();
        Assert.False(await db.Set<AuthenticationOperation>().AnyAsync());
    }

    [Fact]
    public async Task Exchange_and_refresh_keep_legacy_user_data_fields()
    {
        var session = await PrepareAsync();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.Include(x => x.Company).SingleAsync(x => x.ID == fixture.UserID);
            user.Email = "synthetic@example.invalid"; user.Phone = "+12025550123";
            user.Signature = "[{\"Name\":\"synthetic-signature\",\"Blob\":\"synthetic.png\"}]";
            user.Company!.CompanyType = (ShiftSoftware.ShiftEntity.Model.Enums.CompanyTypes)1;
            await db.SaveChangesAsync();
        }
        try
        {
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var verifier = Guid.NewGuid().ToString();
            var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
            var token = await Entity<TokenDTO>(await ExchangeAsync(host.Client, code.Code, verifier));
            var renewed = await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { token.RefreshToken }));
            foreach (var result in new[] { token, renewed })
            {
                Assert.Equal("synthetic@example.invalid", Assert.Single(result.UserData.Emails).Email);
                Assert.Equal("+12025550123", Assert.Single(result.UserData.Phones).Phone);
                Assert.Equal("synthetic-signature", Assert.Single(result.UserData.UserSignature!).Name);
                Assert.Equal((ShiftSoftware.ShiftEntity.Model.Enums.CompanyTypes)1, result.UserData.CompanyType);
                Assert.Equal(fixture.Username, result.UserData.Username);
            }
        }
        finally
        {
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            user.Phone = null; user.Signature = null;
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData("challenge")]
    [InlineData("verifier")]
    [InlineData("purpose")]
    [InlineData("missing-code")]
    [InlineData("missing-app")]
    public async Task Invalid_protocol_or_operation_context_cannot_issue(string scenario)
    {
        var session = await PrepareAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        if (scenario == "challenge")
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(host.Client, session.Token, verifier,
                challenge: IdentityHttpHost.Pkce().Challenge)).StatusCode);
            return;
        }
        if (scenario == "missing-app")
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(host.Client, session.Token, verifier,
                appId: "missing-app")).StatusCode);
            return;
        }
        Guid code;
        if (scenario == "purpose")
        {
            using var staged = new IdentityHttpHost(fixture);
            var challenge = Assert.IsType<ChallengeRequired>(await staged.StartPasswordChangeAsync(session.Token, IdentityHttpHost.Pkce().Challenge));
            await using var db = fixture.CreateContext();
            code = (await db.Set<AuthenticationOperation>().SingleAsync()).ID;
        }
        else
            code = (await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier))).Code;
        Assert.Equal(HttpStatusCode.BadRequest, (await ExchangeAsync(host.Client,
            scenario == "missing-code" ? Guid.NewGuid() : code, scenario == "verifier" ? "invalid" : verifier)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Compatible_refresh_maintains_last_seen_only_on_success(bool existingLog)
    {
        var session = await PrepareAsync();
        await using (var db = fixture.CreateContext())
        {
            await db.Set<ShiftSoftware.ShiftIdentity.Data.Entities.UserLog>().Where(x => x.UserID == fixture.UserID).ExecuteDeleteAsync();
            if (existingLog)
            {
                var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
                user.UserLog = new() { LastSeen = DateTimeOffset.UtcNow.AddDays(-1) };
                await db.SaveChangesAsync();
            }
        }
        var clock = new ControlledClock(DateTimeOffset.UtcNow.AddSeconds(5));
        fixture.Clock = clock;
        try
        {
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var verifier = Guid.NewGuid().ToString();
            var code = await Entity<AuthCodeModel>(await CreateAsync(host.Client, session.Token, verifier));
            var token = await Entity<TokenDTO>(await ExchangeAsync(host.Client, code.Code, verifier));
            await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { token.RefreshToken }));
            var admittedAt = clock.GetUtcNow();
            await using (var db = fixture.CreateContext())
            {
                var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
                Assert.Equal(admittedAt, user.UserLog!.LastSeen);
                Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).SecurityVersion);
            }
            await ChangeAsync("version");
            clock.Advance(TimeSpan.FromSeconds(10));
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { token.RefreshToken })).StatusCode);
            await using (var db = fixture.CreateContext())
                Assert.Equal(admittedAt, (await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID)).UserLog!.LastSeen);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }
}
