using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
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
public sealed class LegacyRefreshCompatibilitySqlTests(SqlIdentityFixture fixture)
{
    [Fact]
    public async Task Deployed_route_exchanges_hash_subject_once_and_keeps_legacy_deadline()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
        using var oldAuthority = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        var legacy = await LegacyLoginAsync(oldAuthority);
        var legacyDeadline = Read(legacy.RefreshToken).ValidTo;
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
            user.UserLog!.LastSeen = DateTimeOffset.UnixEpoch;
            await db.SaveChangesAsync();
        }

        using var response = await RefreshAsync(host.Client, legacy.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var exchanged = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!;
        AssertCompatibility(exchanged, legacyDeadline, external: false, "test-client", "test-api");
        Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(host.Client, legacy.RefreshToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(host.Client, exchanged.RefreshToken)).StatusCode);

        await using var verify = fixture.CreateContext();
        var operation = await verify.Set<AuthenticationOperation>().SingleAsync(x =>
            x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange);
        Assert.Equal(AuthenticationOperationState.Completed, operation.State);
        Assert.Equal(fixture.UserID, operation.UserID);
        Assert.Equal(32, operation.HandleDigest.Length);
        Assert.Equal(1, operation.SecurityVersion);
        Assert.Equal(1, operation.PolicyRevision);
        Assert.Equal(1, operation.FactorGeneration);
        Assert.Equal("test-client", operation.ClientID);
        Assert.Equal("test-api", operation.Audience);
        Assert.Equal(legacyDeadline, operation.ExpiresAt.UtcDateTime);
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyRefreshExchanged").ToListAsync());
        Assert.True((await verify.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID)).UserLog!.LastSeen > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Raw_numeric_subject_is_preserved_for_pre_encoding_sessions()
    {
        var clock = Clock();
        fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync();
            var token = LegacyToken(fixture.UserID.ToString(), clock.GetUtcNow().AddMinutes(20));
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var exchanged = await EntityAsync<TokenDTO>(await RefreshAsync(host.Client, token));
            Assert.Contains(Read(exchanged.RefreshToken).Claims, x => x.Type == "shift_uid" && x.Value == fixture.UserID.ToString());
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Compatibility_session_keeps_ordinary_refresh_and_app_redirect_without_inventing_proof()
    {
        var clock = Clock();
        fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync(mfa: true);
            var deadline = clock.GetUtcNow().AddMinutes(20);
            var token = LegacyToken(fixture.UserID.ToString(), deadline);
            await using (var db = fixture.CreateContext())
            {
                var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
                var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
                var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
                var app = await db.Apps.IgnoreQueryFilters().SingleAsync(x => x.AppId == "other-client");
                user.RequireChangePassword = true; user.Email = "unverified@example.invalid"; user.EmailVerified = false;
                state.LocalMfaRecoveryRequired = true; policy.MfaMandatory = true; policy.RequireVerifiedEmail = true;
                app.AppSecret = null; app.IsDeleted = false; app.RedirectUri = "https://other.invalid/callback";
                await db.SaveChangesAsync();
            }
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var exchanged = await EntityAsync<TokenDTO>(await RefreshAsync(host.Client, token));
            AssertCompatibility(exchanged, deadline.UtcDateTime, external: false, "test-client", "test-api");
            var renewed = await EntityAsync<TokenDTO>(await RefreshAsync(host.Client, exchanged.RefreshToken));
            AssertCompatibility(renewed, deadline.UtcDateTime, external: false, "test-client", "test-api");

            var verifier = Guid.NewGuid().ToString();
            var code = await EntityAsync<AuthCodeModel>(await CreateAppCodeAsync(host.Client, renewed.Token, verifier));
            var appSession = await EntityAsync<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/TokenWithAppIdOnly",
                new GenerateExternalTokenWithAppIdOnlyDTO { AppId = "other-client", AuthCode = code.Code, CodeVerifier = verifier }));
            AssertCompatibility(appSession, deadline.UtcDateTime, external: true, "other-client", "other-client");
            Assert.Equal(HttpStatusCode.OK, (await RefreshAsync(host.Client, appSession.RefreshToken)).StatusCode);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task Current_inactive_or_deleted_state_refuses_without_consuming(bool active, bool deleted)
    {
        var clock = Clock(); fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync();
            var token = LegacyToken(fixture.UserID.ToString(), clock.GetUtcNow().AddMinutes(20));
            await using (var db = fixture.CreateContext())
            {
                var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.ID == fixture.UserID);
                user.IsActive = active; user.IsDeleted = deleted;
                await db.SaveChangesAsync();
            }
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(host.Client, token)).StatusCode);
            await using var verify = fixture.CreateContext();
            Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync());
            Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "LegacyRefreshExchanged"));
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Unready_policy_revision_refuses_without_consuming()
    {
        var clock = Clock(); fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync();
            var token = LegacyToken(fixture.UserID.ToString(), clock.GetUtcNow().AddMinutes(20));
            await using (var db = fixture.CreateContext())
                await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(y => y.Revision, 2));
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await RefreshAsync(host.Client, token)).StatusCode);
            await using var verify = fixture.CreateContext();
            Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync());
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task First_exchange_may_stamp_current_version_but_replay_cannot_stamp_a_later_one()
    {
        var clock = Clock(); fixture.Clock = clock;
        try
        {
            await fixture.ResetAsync();
            var token = LegacyToken(fixture.UserID.ToString(), clock.GetUtcNow().AddMinutes(20));
            await SetVersionAsync(2);
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var exchanged = await EntityAsync<TokenDTO>(await RefreshAsync(host.Client, token));
            Assert.Contains(Read(exchanged.RefreshToken).Claims, x => x.Type == "shift_sv" && x.Value == "2");
            await using (var changed = fixture.CreateContext())
            {
                var state = await changed.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
                var user = await changed.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
                state.SecurityVersion = 3; user.UserLog ??= new(); user.UserLog.LastSeen = DateTimeOffset.UnixEpoch;
                await changed.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(host.Client, token)).StatusCode);
            await using var db = fixture.CreateContext();
            var operation = await db.Set<AuthenticationOperation>().SingleAsync(x =>
                x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange);
            Assert.Equal(2, operation.SecurityVersion);
            Assert.Single(await db.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyRefreshExchanged").ToListAsync());
            Assert.Equal(DateTimeOffset.UnixEpoch,
                (await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID)).UserLog!.LastSeen);
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Fact]
    public async Task Exchange_tombstone_survives_until_legacy_deadline_then_cleanup_removes_it()
    {
        var clock = Clock(); fixture.Clock = clock;
        fixture.LegacyRefreshLifetimeSeconds = 3 * 24 * 60 * 60;
        try
        {
            await fixture.ResetAsync();
            var token = LegacyToken(fixture.UserID.ToString(), clock.GetUtcNow().AddDays(2));
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
            var exchanged = await EntityAsync<TokenDTO>(await RefreshAsync(host.Client, token));
            clock.Advance(TimeSpan.FromHours(25));
            var renewed = await EntityAsync<TokenDTO>(await RefreshAsync(host.Client, exchanged.RefreshToken));
            await using (var db = fixture.CreateContext())
            {
                Assert.Equal(0, await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow()));
                Assert.True(await db.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange));
            }
            clock.Advance(TimeSpan.FromHours(24));
            Assert.Equal(HttpStatusCode.BadRequest, (await RefreshAsync(host.Client, renewed.RefreshToken)).StatusCode);
            await using (var db = fixture.CreateContext())
            {
                Assert.Equal(0, await new SqlIdentitySecurityStore(db).CleanupAsync(clock.GetUtcNow()));
                Assert.False(await db.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange));
            }
        }
        finally { fixture.Clock = TimeProvider.System; fixture.LegacyRefreshLifetimeSeconds = 1800; }
    }

    private async Task SetVersionAsync(long version)
    {
        await using var db = fixture.CreateContext();
        await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
            .ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, version));
    }

    private async Task<TokenDTO> LegacyLoginAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host) =>
        await EntityAsync<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Login",
            new LoginDTO { Username = fixture.Username, Password = fixture.Password }));

    private string LegacyToken(string subject, DateTimeOffset expires, DateTimeOffset? notBefore = null,
        string issuer = "https://legacy.invalid", string audience = "legacy-refresh", string? key = null,
        string algorithm = SecurityAlgorithms.HmacSha512Signature, IEnumerable<Claim>? extraClaims = null)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, subject) }.Concat(extraClaims ?? []);
        var jwt = new JwtSecurityToken(issuer, audience, claims, notBefore?.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? fixture.LegacyRefreshKey)), algorithm));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refresh) =>
        client.PostAsJsonAsync("/api/Auth/Refresh", new { RefreshToken = refresh });

    private static Task<HttpResponseMessage> CreateAppCodeAsync(HttpClient client, string access, string verifier)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/AuthCode");
        request.Headers.Authorization = new("Bearer", access);
        request.Content = JsonContent.Create(new GenerateAuthCodeDTO
            { AppId = "other-client", ReturnUrl = "/orders", CodeChallenge = HashService.SHA512GenerateHash(verifier) });
        return client.SendAsync(request);
    }

    private static async Task<T> EntityAsync<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<T>>())!.Entity!;
        }
    }

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);
    private static ControlledClock Clock() => new(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));

    private static void AssertCompatibility(TokenDTO token, DateTime deadline, bool external, string client, string audience)
    {
        foreach (var value in new[] { token.Token, token.RefreshToken })
        {
            var jwt = Read(value);
            Assert.True(jwt.ValidTo <= deadline);
            Assert.Contains(jwt.Claims, x => x.Type == "shift_legacy_until" && x.Value == new DateTimeOffset(deadline).ToUnixTimeSeconds().ToString());
            Assert.Contains(jwt.Claims, x => x.Type == "shift_mfa" && x.Value == "false");
            Assert.Contains(jwt.Claims, x => x.Type == "auth_time" && x.Value == "0");
            Assert.Contains(jwt.Claims, x => x.Type == "shift_client" && x.Value == client);
            Assert.Contains(jwt.Claims, x => x.Type == "shift_resource" && x.Value == audience);
            Assert.Contains(jwt.Claims, x => x.Type == "shift_external" && x.Value == external.ToString().ToLowerInvariant());
        }
    }
}
