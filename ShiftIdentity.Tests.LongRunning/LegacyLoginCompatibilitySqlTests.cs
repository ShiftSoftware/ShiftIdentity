using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Auth;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
[Trait("Category", "Http")]
public sealed class LegacyLoginCompatibilitySqlTests(SqlIdentityFixture fixture)
{
    private const string NewPassword = "Another Synthetic Password 8!";

    [Fact]
    public async Task Old_host_login_redirect_exchange_and_refresh_keep_envelopes_and_real_proof()
    {
        await Prepare();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            user.Email = "synthetic@example.invalid"; user.Phone = "+12025550123";
            user.Signature = "[{\"Name\":\"signature\",\"Blob\":\"synthetic.png\"}]";
            await db.SaveChangesAsync();
        }
        using var host = Host();
        using var response = await Login(host.Client);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(json.RootElement.TryGetProperty("kind", out _));
        var session = await Entity<TokenDTO>(response);
        Assert.Equal(AuthPurpose.None, session.Flow);
        Assert.Equal(fixture.UserID.ToString(), session.UserData.ID);
        Assert.Equal(fixture.Username, session.UserData.Username);
        Assert.Equal("synthetic@example.invalid", Assert.Single(session.UserData.Emails).Email);
        Assert.Equal("+12025550123", Assert.Single(session.UserData.Phones).Phone);
        Assert.Single(session.UserData.UserSignature!);
        var claims = Jwt(session.Token);
        Assert.Equal("2", claims["shift_schema"]); Assert.Equal("1", claims["shift_sv"]);
        Assert.Equal("false", claims["shift_mfa"]);
        Assert.InRange(long.Parse(claims["auth_time"]), DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(), DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.False(claims.ContainsKey("shift_legacy_until"));
        var refreshed = await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { session.RefreshToken }));
        Assert.Equal(claims["auth_time"], Jwt(refreshed.Token)["auth_time"]);
        var verifier = Guid.NewGuid().ToString();
        var code = await Entity<AuthCodeModel>(await Send(host.Client, HttpMethod.Post, "/api/Auth/AuthCode", session.Token,
            new GenerateAuthCodeDTO { AppId = "other-client", ReturnUrl = "/orders", CodeChallenge = Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(verifier))) }));
        Assert.Equal("/orders", code.ReturnUrl); Assert.Equal("https://other.invalid/callback", code.RedirectUri);
        using var otherNode = Host();
        var external = await Entity<TokenDTO>(await otherNode.Client.PostAsJsonAsync("/api/Auth/TokenWithAppIdOnly",
            new GenerateExternalTokenWithAppIdOnlyDTO { AppId = "other-client", AuthCode = code.Code, CodeVerifier = verifier }));
        Assert.Equal("true", Jwt(external.Token)["shift_external"]);
        Assert.Equal(claims["auth_time"], Jwt(external.Token)["auth_time"]);
        await Entity<TokenDTO>(await otherNode.Client.PostAsJsonAsync("/api/Auth/Refresh", new { external.RefreshToken }));
        await using var verify = fixture.CreateContext();
        var userAfter = await verify.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
        Assert.True(HashService.VerifyVersionedPassword(fixture.Password, userAfter.Salt, userAfter.PasswordHash));
        Assert.False(VersionedPasswordHash.NeedsUpgrade(userAfter.PasswordHash));
        Assert.True(userAfter.UserLog!.LastSeen > DateTimeOffset.UnixEpoch);
        Assert.Single(await verify.Set<AuthenticationAuditEvent>().Where(x => x.Outcome == "LegacyLoginPasswordProven").ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Enrolled_factor_requires_real_Mfa_and_replay_is_refused(bool forcedChange)
    {
        await Prepare(true, forcedChange);
        using var host = Host();
        var step = await Entity<TokenDTO>(await Login(host.Client));
        Assert.Equal(forcedChange ? AuthPurpose.ChangePassword : AuthPurpose.Mfa, step.Flow);
        Assert.Null(step.RefreshToken); Assert.Null(step.RefreshTokenLifeTimeInSeconds);
        if (forcedChange)
        {
            // The accepted staged transition defers the password mutation until all required proofs pass.
            step = await Entity<TokenDTO>(await Change(host.Client, step.Token));
            Assert.Equal(AuthPurpose.Mfa, step.Flow);
            await using var beforeMfa = fixture.CreateContext();
            Assert.True((await beforeMfa.Users.SingleAsync(x => x.ID == fixture.UserID)).RequireChangePassword);
        }
        using var other = Host();
        var session = await Entity<TokenDTO>(await Mfa(other.Client, step.Token));
        Assert.Equal(AuthPurpose.None, session.Flow); Assert.Equal("true", Jwt(session.Token)["shift_mfa"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mfa(host.Client, step.Token)).StatusCode);
        await Entity<TokenDTO>(await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { session.RefreshToken }));
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.False(user.RequireChangePassword);
        Assert.True(HashService.VerifyVersionedPassword(forcedChange ? NewPassword : fixture.Password, user.Salt, user.PasswordHash));
        Assert.NotNull((await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).LastAcceptedTotpStep);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mandatory_enrollment_uses_committed_secret_and_existing_TotpDTO(bool forcedChange)
    {
        await Prepare(false, forcedChange, mandatory: true);
        using var first = Host();
        var step = await Entity<TokenDTO>(await Login(first.Client));
        if (forcedChange) step = await Entity<TokenDTO>(await Change(first.Client, step.Token));
        Assert.Equal(AuthPurpose.MfaEnrollment, step.Flow);
        using var second = Host();
        var setup = await Entity<TotpDTO>(await Send(second.Client, HttpMethod.Get, "/api/UserManager/StartTotpEnrollment", step.Token));
        var again = await Entity<TotpDTO>(await Send(first.Client, HttpMethod.Get, "/api/UserManager/StartTotpEnrollment", step.Token));
        Assert.Equal(setup.Secret, again.Secret); Assert.Equal(setup.SasToken, again.SasToken);
        Assert.Contains("otpauth://", setup.Uri); Assert.Contains("<svg", setup.Svg);
        setup.Code = new Totp(Base32Encoding.ToBytes(setup.Secret)).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
        var wrongSignature = setup.SasToken; setup.SasToken = "tampered";
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(second.Client, HttpMethod.Post, "/api/UserManager/ConfirmTotpEnrollment", step.Token, setup)).StatusCode);
        setup.SasToken = wrongSignature;
        var session = await Entity<TokenDTO>(await Send(second.Client, HttpMethod.Post, "/api/UserManager/ConfirmTotpEnrollment", step.Token, setup));
        Assert.Equal("true", Jwt(session.Token)["shift_mfa"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(first.Client, HttpMethod.Post, "/api/UserManager/ConfirmTotpEnrollment", step.Token, setup)).StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Null((await db.Users.SingleAsync(x => x.ID == fixture.UserID)).TotpSecret);
        Assert.NotNull((await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID)).ProtectedTotpSecret);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("deleted")]
    [InlineData("locked")]
    [InlineData("recovery")]
    [InlineData("email")]
    [InlineData("budget")]
    public async Task Restrictions_never_become_an_ordinary_session(string restriction)
    {
        await Prepare();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            if (restriction == "inactive") user.IsActive = false;
            if (restriction == "deleted") user.IsDeleted = true;
            if (restriction == "locked") user.LockDownUntil = fixture.Clock.GetUtcNow().AddMinutes(5).UtcDateTime;
            if (restriction == "recovery") state.LocalMfaRecoveryRequired = true;
            if (restriction == "email") { user.Email = "synthetic@example.invalid"; (await db.Set<AuthenticationPolicyState>().SingleAsync()).RequireVerifiedEmail = true; }
            if (restriction == "budget") { state.FailedProofs = 10; state.FailureWindowStart = fixture.Clock.GetUtcNow(); }
            await db.SaveChangesAsync();
        }
        using var host = Host();
        using var response = await Login(host.Client);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
        await using var verify = fixture.CreateContext();
        Assert.False(await verify.Set<AuthenticationAuditEvent>().AnyAsync(x => x.Outcome == "SessionIssued"));
    }

    [Fact]
    public async Task Legacy_counter_order_and_successful_password_LastSeen_are_preserved()
    {
        await Prepare(true);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            user.LoginAttempts = 9; user.IsActive = false;
            await db.SaveChangesAsync();
        }
        using var host = Host();
        Assert.Equal(HttpStatusCode.BadRequest, (await Login(host.Client, "wrong")).StatusCode);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
            Assert.Equal(0, user.LoginAttempts); Assert.True(user.LockDownUntil > DateTime.UtcNow);
            Assert.Equal(DateTimeOffset.UnixEpoch, user.UserLog!.LastSeen);
            user.IsActive = true; user.LockDownUntil = DateTime.UtcNow.AddSeconds(-1); user.LoginAttempts = 3;
            await db.SaveChangesAsync();
        }
        var step = await Entity<TokenDTO>(await Login(host.Client));
        Assert.Equal(AuthPurpose.Mfa, step.Flow);
        await using var verify = fixture.CreateContext();
        var after = await verify.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
        Assert.Equal(0, after.LoginAttempts); Assert.Null(after.LockDownUntil);
        Assert.True(after.UserLog!.LastSeen > DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task Forced_change_still_requires_current_password_and_matching_confirmation()
    {
        await Prepare(false, true);
        using var host = Host();
        var step = await Entity<TokenDTO>(await Login(host.Client));
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(host.Client, step.Token, "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Send(host.Client, HttpMethod.Put, "/api/UserManager/ChangePassword", step.Token,
            new ChangePasswordDTO { CurrentPassword = fixture.Password, NewPassword = NewPassword, ConfirmPassword = "different" })).StatusCode);
        var session = await Entity<TokenDTO>(await Change(host.Client, step.Token));
        Assert.Equal(AuthPurpose.None, session.Flow); Assert.Equal("false", Jwt(session.Token)["shift_mfa"]);
        Assert.Equal(HttpStatusCode.BadRequest, (await Change(host.Client, step.Token)).StatusCode);
    }

    [Fact]
    public async Task Bare_user_and_Mfa_flag_cannot_issue_with_staged_services()
    {
        await Prepare();
        using var host = Host(); using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AuthService>();
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.Null(service.IssueLoginToken(user, true));
        Assert.Null(await service.MfaLogin(user.ID.ToString(), "123456"));
        var result = await service.LoginAsync(new() { Username = fixture.Username, Password = fixture.Password });
        Assert.Equal(LoginResultEnum.Success, result.Result); Assert.Equal("2", Jwt(result.Token.Token)["shift_schema"]);
    }

    [Theory]
    [InlineData("security")]
    [InlineData("policy")]
    [InlineData("stale-policy")]
    public async Task Missing_authoritative_state_or_stale_configuration_refuses_without_legacy_fallback(string missing)
    {
        await Prepare();
        await using (var db = fixture.CreateContext())
        {
            if (missing == "security") await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID).ExecuteDeleteAsync();
            if (missing == "policy") await db.Set<AuthenticationPolicyState>().ExecuteDeleteAsync();
            if (missing == "stale-policy") await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.Revision, 2));
        }
        try
        {
            using var host = Host();
            using var response = await Login(host.Client);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Null((await response.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity);
            await using var db = fixture.CreateContext();
            Assert.Equal(DateTimeOffset.UnixEpoch, (await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID)).UserLog!.LastSeen);
        }
        finally
        {
            await using var db = fixture.CreateContext();
            if (missing == "security") db.Set<UserSecurityState>().Add(new() { UserID = fixture.UserID });
            if (missing == "policy") db.Set<AuthenticationPolicyState>().Add(new());
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Disabled_Mfa_policy_does_not_invent_Mfa_proof_from_an_enrolled_factor()
    {
        await Prepare(true);
        await using (var db = fixture.CreateContext())
            await db.Set<AuthenticationPolicyState>().ExecuteUpdateAsync(x => x.SetProperty(p => p.MfaEnabled, false));
        using var host = Host();
        var session = await Entity<TokenDTO>(await Login(host.Client));
        Assert.Equal(AuthPurpose.None, session.Flow); Assert.Equal("false", Jwt(session.Token)["shift_mfa"]);
    }

    [Fact]
    public async Task Restricted_bearer_cannot_access_resources_refresh_exchange_or_wrong_steps()
    {
        await Prepare(true);
        using var host = Host();
        var step = await Entity<TokenDTO>(await Login(host.Client));
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(host.Client, HttpMethod.Get, "/api/UserManager/UserData", step.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("/api/Auth/Refresh", new { RefreshToken = step.Token })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Change(host.Client, step.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Send(host.Client, HttpMethod.Get, "/api/UserManager/StartTotpEnrollment", step.Token)).StatusCode);
    }

    internal async Task Prepare(bool mfa = false, bool forced = false, bool mandatory = false)
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync(mfa);
        await using var db = fixture.CreateContext();
        var user = await db.Users.Include(x => x.UserLog).SingleAsync(x => x.ID == fixture.UserID);
        user.RequireChangePassword = forced; user.LoginAttempts = 0;
        user.UserLog ??= new(); user.UserLog.LastSeen = DateTimeOffset.UnixEpoch;
        (await db.Set<AuthenticationPolicyState>().SingleAsync()).MfaMandatory = mandatory;
        await db.SaveChangesAsync();
    }
    internal LegacyIdentityHttpHost<IdentityTestDbContext> Host() => new(fixture, authority: true);
    internal Task<HttpResponseMessage> Login(HttpClient client, string? password = null) => client.PostAsJsonAsync("/api/Auth/Login",
        new LoginDTO { Username = fixture.Username, Password = password ?? fixture.Password });
    internal Task<HttpResponseMessage> Mfa(HttpClient client, string token) => Send(client, HttpMethod.Post, "/api/Auth/Login/mfa", token,
        new { Code = new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime) });
    internal Task<HttpResponseMessage> Change(HttpClient client, string token, string? current = null) => Send(client, HttpMethod.Put,
        "/api/UserManager/ChangePassword", token, new ChangePasswordDTO
        { CurrentPassword = current ?? fixture.Password, NewPassword = NewPassword, ConfirmPassword = NewPassword });
    internal static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string token, object? dto = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new("Bearer", token);
        if (dto is not null) request.Content = JsonContent.Create(dto);
        return await client.SendAsync(request);
    }
    internal static async Task<T> Entity<T>(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            return (await response.Content.ReadFromJsonAsync<ShiftEntityResponse<T>>())!.Entity!;
        }
    }
    internal static Dictionary<string, string> Jwt(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token).Claims
        .Where(x => x.Type.StartsWith("shift_", StringComparison.Ordinal) || x.Type == "auth_time").ToDictionary(x => x.Type, x => x.Value);
}
