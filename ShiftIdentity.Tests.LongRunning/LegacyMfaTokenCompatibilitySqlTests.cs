using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;
using static ShiftIdentity.Tests.LegacyLoginCompatibilitySqlTests;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
[Trait("Category", "Http")]
public sealed class LegacyMfaTokenCompatibilitySqlTests(SqlIdentityFixture fixture)
{
    [Fact]
    public async Task Pre_cutover_Mfa_credential_from_the_old_issuer_completes_once_through_the_deployed_route()
    {
        var step = await PrepareAsync();
        var legacyDeadline = new JwtSecurityTokenHandler().ReadJwtToken(step.Token).ValidTo;
        using var host = Staged();
        using var response = await Mfa(host.Client, step.Token);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        var session = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!;
        Assert.Equal(AuthPurpose.None, session.Flow);
        Assert.NotNull(session.RefreshToken); Assert.NotNull(session.RefreshTokenLifeTimeInSeconds);
        Assert.Equal(fixture.UserID.ToString(), session.UserData.ID);
        Assert.Equal(fixture.Username, session.UserData.Username);
        var claims = Jwt(session.Token);
        Assert.Equal("2", claims["shift_schema"]); Assert.Equal("1", claims["shift_sv"]);
        Assert.Equal("true", claims["shift_mfa"]); Assert.Equal("0", claims["auth_time"]);
        Assert.Equal("false", claims["shift_external"]); Assert.False(claims.ContainsKey("shift_legacy_until"));
        // Replay of the consumed credential, on this host or another, yields no second pair.
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host.Client, step.Token)).StatusCode);
        using var other = Staged();
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(other.Client, step.Token)).StatusCode);
        // The session is an ordinary v2 session: it renews and transfers to an app with its real MFA proof and no invented time.
        var renewed = await Entity<TokenDTO>(await other.Client.PostAsJsonAsync("/api/Auth/Refresh", new { session.RefreshToken }));
        Assert.Equal("true", Jwt(renewed.Token)["shift_mfa"]); Assert.Equal("0", Jwt(renewed.Token)["auth_time"]);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await Send(other.Client, HttpMethod.Post, "/api/Auth/AuthCode", renewed.Token,
            new GenerateAuthCodeDTO { AppId = "other-client", ReturnUrl = "/orders", CodeChallenge = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(verifier))) }));
        var external = await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/TokenWithAppIdOnly",
            new GenerateExternalTokenWithAppIdOnlyDTO { AppId = "other-client", AuthCode = code.Code, CodeVerifier = verifier }));
        Assert.Equal("true", Jwt(external.Token)["shift_external"]); Assert.Equal("true", Jwt(external.Token)["shift_mfa"]);
        Assert.Equal("0", Jwt(external.Token)["auth_time"]);
        await using var verify = fixture.CreateContext();
        var operation = await verify.Set<AuthenticationOperation>().SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange);
        Assert.Equal(AuthenticationOperationState.Completed, operation.State);
        Assert.Equal(fixture.UserID, operation.UserID); Assert.Equal(32, operation.HandleDigest.Length);
        Assert.Equal(1, operation.SecurityVersion); Assert.Equal(1, operation.PolicyRevision); Assert.Equal(1, operation.FactorGeneration);
        Assert.Equal("test-client", operation.ClientID); Assert.Equal("test-api", operation.Audience); Assert.False(operation.External);
        Assert.Equal(legacyDeadline, operation.ExpiresAt.UtcDateTime);
        Assert.Null(operation.PasswordProvenAt); Assert.NotNull(operation.MfaProvenAt); Assert.Equal(0, operation.FailedAttempts);
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyMfaChallenge").ToListAsync());
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "MfaCompleted").ToListAsync());
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyMfaExchanged").ToListAsync());
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "SessionIssued").ToListAsync());
        var state = await verify.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        Assert.NotNull(state.LastAcceptedTotpStep); Assert.Equal(0, state.FailedProofs);
    }

    [Fact]
    public async Task Wrong_codes_count_against_the_credential_and_the_shared_budget_until_it_locks()
    {
        var step = await PrepareAsync();
        using var host = Staged();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var refused = await Mfa(host.Client, step.Token, Wrong(Code()));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Null((await refused.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        }
        await using (var db = fixture.CreateContext())
        {
            var operation = await db.Set<AuthenticationOperation>().SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange);
            Assert.Equal(AuthenticationOperationState.Locked, operation.State); Assert.Equal(5, operation.FailedAttempts);
            Assert.Equal(32, operation.HandleDigest.Length);
            Assert.Equal(5, (await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).FailedProofs);
            Assert.Equal(5, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "InvalidMfa"));
        }
        // The right code no longer helps: the credential row is locked, and a fresh login is the only way forward.
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host.Client, step.Token)).StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued" || x.Outcome == "LegacyMfaExchanged"));
    }

    [Fact]
    public async Task Two_pre_cutover_credentials_exchange_independently_under_one_factor_replay_control()
    {
        var first = await PrepareAsync();
        // The deployed credential carries no unique id or issue time, only a whole-second expiry, so two logins in the
        // same second are one and the same credential. Wait for the next second to hold two distinct ones.
        var second = await LegacyLoginAsync();
        for (var attempt = 0; second.Token == first.Token && attempt < 10; attempt++)
        {
            await Task.Delay(250);
            second = await LegacyLoginAsync();
        }
        Assert.NotEqual(first.Token, second.Token);
        // The clock starts after both credentials exist, so their lifetimes stay within the configured bound.
        var clock = new ControlledClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        fixture.Clock = clock;
        try
        {
            using var host = Staged();
            var code = Code(clock.GetUtcNow());
            await Entity<TokenDTO>(await Mfa(host.Client, first.Token, code));
            // The same factor step cannot be spent twice, whichever credential presents it.
            using var replay = await Mfa(host.Client, second.Token, code);
            Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
            clock.Advance(TimeSpan.FromSeconds(30));
            await Entity<TokenDTO>(await Mfa(host.Client, second.Token, Code(clock.GetUtcNow())));
            await using var db = fixture.CreateContext();
            Assert.Equal(2, await db.Set<AuthenticationOperation>().CountAsync(x =>
                x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange && x.State == AuthenticationOperationState.Completed));
            Assert.Equal(2, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "LegacyMfaExchanged"));
            Assert.Equal(2, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "SessionIssued"));
            Assert.Equal(1, await db.Set<AuthenticationAuditEvent>().CountAsync(x => x.Outcome == "InvalidMfa"));
        }
        finally { fixture.Clock = TimeProvider.System; }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("locked")]
    [InlineData("recovery")]
    [InlineData("no-factor")]
    [InlineData("forced-change")]
    [InlineData("mfa-disabled")]
    [InlineData("budget")]
    [InlineData("stale-policy")]
    public async Task Changed_account_state_after_issuance_refuses_without_a_session_or_a_row(string change)
    {
        var step = await PrepareAsync();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
            switch (change)
            {
                case "inactive": user.IsActive = false; break;
                case "deleted": user.IsDeleted = true; break;
                case "locked": user.LockDownUntil = DateTime.UtcNow.AddMinutes(5); break;
                case "recovery": state.LocalMfaRecoveryRequired = true; break;
                case "no-factor": fixture.SetSyntheticFactor(state, null); break;
                case "forced-change": user.RequireChangePassword = true; break;
                case "mfa-disabled": policy.MfaEnabled = false; break;
                case "budget": state.FailedProofs = 10; state.FailureWindowStart = DateTimeOffset.UtcNow; break;
                case "stale-policy": policy.Revision = 2; break;
            }
            await db.SaveChangesAsync();
        }
        try
        {
            using var host = Staged();
            using var response = await Mfa(host.Client, step.Token);
            Assert.Equal(change == "stale-policy" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.BadRequest, response.StatusCode);
            var body = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!;
            Assert.Null(body.Entity);
            if (change == "forced-change") Assert.Contains("sign in again", body.Message!.Body, StringComparison.OrdinalIgnoreCase);
            if (change == "recovery") Assert.Contains("recovery", body.Message!.Body, StringComparison.OrdinalIgnoreCase);
            await using var verify = fixture.CreateContext();
            Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued" || x.Outcome == "LegacyMfaExchanged"));
            Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange));
            Assert.Null((await verify.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).LastAcceptedTotpStep);
        }
        finally
        {
            if (change == "stale-policy")
            {
                await using var db = fixture.CreateContext();
                await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, 1));
            }
        }
    }

    [Fact]
    public async Task Version_change_after_the_first_presentation_refuses_the_pending_credential()
    {
        var step = await PrepareAsync();
        using var host = Staged();
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host.Client, step.Token, Wrong(Code()))).StatusCode);
        await using (var db = fixture.CreateContext())
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, 2));
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host.Client, step.Token)).StatusCode);
        await using var verify = fixture.CreateContext();
        var operation = await verify.Set<AuthenticationOperation>().SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange);
        Assert.Equal(AuthenticationOperationState.AwaitingMfa, operation.State);
        Assert.Equal(1, operation.SecurityVersion); Assert.Equal(1, operation.FailedAttempts);
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued"));
    }

    [Fact]
    public async Task Verified_email_gate_after_a_real_factor_proof_consumes_the_credential_without_a_session()
    {
        var step = await PrepareAsync();
        await using (var db = fixture.CreateContext())
        {
            (await db.Users.SingleAsync(x => x.ID == fixture.UserID)).Email = "synthetic@example.invalid";
            (await db.Set<AuthenticationPolicyState>().SingleAsync()).RequireVerifiedEmail = true;
            await db.SaveChangesAsync();
        }
        using var host = Staged();
        using var response = await Mfa(host.Client, step.Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!;
        Assert.Null(body.Entity); Assert.Contains("email", body.Message!.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host.Client, step.Token)).StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Equal(AuthenticationOperationState.Completed, (await verify.Set<AuthenticationOperation>()
            .SingleAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange)).State);
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyMfaExchanged").ToListAsync());
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued"));
    }

    [Theory]
    [InlineData("change-password")]
    [InlineData("enrollment")]
    [InlineData("expired")]
    [InlineData("wrong-key")]
    [InlineData("garbage")]
    public async Task Other_or_invalid_pre_cutover_credentials_are_not_admitted_to_the_Mfa_route(string kind)
    {
        await PrepareAsync();
        var token = kind switch
        {
            "change-password" => LegacyToken(AuthPurpose.ChangePassword, DateTimeOffset.UtcNow.AddMinutes(4)),
            "enrollment" => LegacyToken(AuthPurpose.MfaEnrollment, DateTimeOffset.UtcNow.AddMinutes(4)),
            "expired" => LegacyToken(AuthPurpose.Mfa, DateTimeOffset.UtcNow.AddSeconds(-1)),
            "wrong-key" => LegacyToken(AuthPurpose.Mfa, DateTimeOffset.UtcNow.AddMinutes(4), Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))),
            _ => "not-a-token"
        };
        using var host = Staged();
        using var response = await Mfa(host.Client, token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync(x => x.Purpose == AuthenticationOperationPurpose.LegacyMfaExchange));
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued"));
    }

    [Fact]
    public async Task Hand_minted_credential_with_the_numeric_subject_form_is_exchanged_like_the_issued_one()
    {
        await PrepareAsync();
        var token = LegacyToken(AuthPurpose.Mfa, DateTimeOffset.UtcNow.AddMinutes(4), subject: fixture.UserID.ToString());
        using var host = Staged();
        var session = await Entity<TokenDTO>(await Mfa(host.Client, token));
        Assert.Equal("true", Jwt(session.Token)["shift_mfa"]);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(session.RefreshToken).Claims, x => x.Type == "shift_uid" && x.Value == fixture.UserID.ToString());
    }

    [Fact]
    public async Task Without_staged_services_the_deployed_route_still_verifies_through_the_legacy_issuer()
    {
        var step = await PrepareAsync();
        fixture.LegacyMfaEnabled = true;
        try
        {
            using var old = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
            Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(old.Client, step.Token, Wrong(Code()))).StatusCode);
            var session = await Entity<TokenDTO>(await Mfa(old.Client, step.Token));
            Assert.Equal(AuthPurpose.None, session.Flow); Assert.NotNull(session.RefreshToken);
            Assert.False(Jwt(session.Token).ContainsKey("shift_schema"));
        }
        finally { fixture.LegacyMfaEnabled = false; }
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationOperation>().AnyAsync());
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync());
    }

    internal async Task<TokenDTO> PrepareAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync(mfa: true);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
            // The retained legacy column the old issuer and verifier read; the staged host reads only the protected copy.
            user.TotpSecret = fixture.FactorSecret;
            user.UserLog ??= new(); user.UserLog.LastSeen = DateTimeOffset.UnixEpoch;
            await db.SaveChangesAsync();
        }
        return await LegacyLoginAsync();
    }

    /// <summary>A password login on an unstaged host with the deployed MFA policy: the old issuer's own MFA step credential.</summary>
    internal async Task<TokenDTO> LegacyLoginAsync()
    {
        fixture.LegacyMfaEnabled = true;
        try
        {
            using var issuer = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
            var step = await Entity<TokenDTO>(await issuer.Client.PostAsJsonAsync("/api/Auth/Login",
                new LoginDTO { Username = fixture.Username, Password = fixture.Password }));
            Assert.Equal(AuthPurpose.Mfa, step.Flow); Assert.Null(step.RefreshToken);
            return step;
        }
        finally { fixture.LegacyMfaEnabled = false; }
    }

    internal LegacyIdentityHttpHost<IdentityTestDbContext> Staged(Action<string>? observe = null, params IInterceptor[] interceptors) =>
        new(fixture, true, observe, interceptors);

    internal string Code(DateTimeOffset? at = null) =>
        new Totp(fixture.FactorSecret).ComputeTotp((at ?? fixture.Clock.GetUtcNow()).UtcDateTime);

    internal static string Wrong(string code) => code == "000000" ? "000001" : "000000";

    internal Task<HttpResponseMessage> Mfa(HttpClient client, string token, string? code = null) =>
        Send(client, HttpMethod.Post, "/api/Auth/Login/mfa", token, new { Code = code ?? Code() });

    internal string LegacyToken(AuthPurpose purpose, DateTimeOffset expires, string? key = null, string? subject = null)
    {
        subject ??= new HashIdService(Options.Create(new ShiftEntityOptions())).Encode<UserDTO>(fixture.UserID);
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject), new Claim(ClaimTypes.Name, fixture.Username),
            new Claim(ClaimTypes.GivenName, "Synthetic User"), new Claim(ShiftIdentityClaims.TokenPurpose, purpose.ToString())
        };
        var jwt = new JwtSecurityToken("https://legacy.invalid", "legacy-temporary", claims, null, expires.UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key ?? fixture.LegacyTemporaryKey)), SecurityAlgorithms.HmacSha512Signature));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
