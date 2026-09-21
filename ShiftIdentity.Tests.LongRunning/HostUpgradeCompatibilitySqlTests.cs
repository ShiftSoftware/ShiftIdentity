using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
public sealed class HostUpgradeCompatibilitySqlTests(SqlIdentityFixture fixture)
{
    [Fact]
    public async Task One_refresh_key_supports_legacy_exchange_renewal_and_revocation_without_legacy_fallback()
    {
        await fixture.ResetAsync();
        void Configure(ShiftSoftware.ShiftIdentity.Core.ShiftIdentityConfiguration c)
        {
            c.Authority.RefreshKey = null;
            c.RefreshToken.Issuer = c.Token.Issuer;
            // Use the same issuer, audience and signing bytes in both formats. Only the credential format differs.
            c.Authority.RefreshAudience = c.RefreshToken.Audience;
        }
        string legacyToken;
        string newToken;
        using (var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, Configure))
        {
            var settings = host.Settings.RefreshToken;
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key));
            var handler = new JwtSecurityTokenHandler();
            legacyToken = handler.WriteToken(new JwtSecurityToken(settings.Issuer, settings.Audience,
                [new Claim(ClaimTypes.NameIdentifier, fixture.UserID.ToString())], expires: DateTime.UtcNow.AddMinutes(20),
                signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha512Signature)));
            using var exchanged = await host.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = legacyToken });
            Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
            newToken = (await exchanged.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!.RefreshToken;
            var principal = handler.ValidateToken(newToken, new TokenValidationParameters
            {
                ValidIssuer = settings.Issuer, ValidAudience = settings.Audience, IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha512], ClockSkew = TimeSpan.Zero
            }, out _);
            Assert.Equal("2", principal.FindFirst("shift_schema")!.Value);
            Assert.Null(new LegacyRefreshTokenCodec(settings, fixture.Clock).Validate(newToken));
            Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = newToken })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = legacyToken })).StatusCode);
            await using var db = fixture.CreateContext();
            await db.Set<UserSecurityState>().Where(x => x.UserID == fixture.UserID)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.SecurityVersion, y => y.SecurityVersion + 1));
            Assert.Equal(HttpStatusCode.BadRequest, (await host.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = newToken })).StatusCode);
        }
        using var restarted = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, Configure);
        Assert.Equal(HttpStatusCode.BadRequest, (await restarted.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = newToken })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await restarted.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = legacyToken })).StatusCode);
        await using var verify = fixture.CreateContext();
        Assert.Single(await verify.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange).ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Sharing_the_refresh_key_does_not_accept_another_issuer_or_audience(bool differentIssuer)
    {
        await fixture.ResetAsync();
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture,
            c => c.Authority.RefreshKey = null);
        var settings = host.Settings.RefreshToken;
        var token = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            differentIssuer ? "https://other.invalid" : settings.Issuer,
            differentIssuer ? settings.Audience : "mobile-apps",
            [new Claim(ClaimTypes.NameIdentifier, fixture.UserID.ToString())], expires: DateTime.UtcNow.AddMinutes(20),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(settings.Key)), SecurityAlgorithms.HmacSha512Signature)));
        using var response = await host.Client.PostAsJsonAsync("api/Auth/Refresh", new RefreshDTO { RefreshToken = token });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.LegacyRefreshExchange).ToListAsync());
    }

    [Fact]
    public async Task Host_startup_applies_the_existing_TOTP_parameters_and_advances_policy_only_on_change()
    {
        await fixture.ResetAsync();
        static void Configure(ShiftSoftware.ShiftIdentity.Core.ShiftIdentityConfiguration c)
        {
            c.MfaSettings.Totp.Digits = 8; c.MfaSettings.Totp.Period = 60;
            c.MfaSettings.Totp.VerificationWindowPast = 2; c.MfaSettings.Totp.VerificationWindowFuture = 0;
        }
        using (var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, Configure)) { }
        using (var restarted = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, Configure)) { }
        await using var db = fixture.CreateContext();
        var policy = await db.Set<AuthenticationPolicyState>().SingleAsync();
        Assert.Equal(2, policy.Revision); Assert.Equal(8, policy.TotpDigits); Assert.Equal(60, policy.TotpPeriodSeconds);
        Assert.Equal(2, policy.TotpWindowPast); Assert.Equal(0, policy.TotpWindowFuture);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task User_list_and_view_read_current_factor_state(bool legacy, bool current)
    {
        await fixture.ResetAsync(mfa: current);
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            user.TotpSecret = legacy ? fixture.FactorSecret : null;
            if (!current)
            {
                var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
                state.LocalMfaRecoveryRequired = true;
                state.FactorGeneration = 2;
            }
            await db.SaveChangesAsync();
        }
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture);
        using var scope = host.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<UserRepository>();
        var entity = await repository.FindAsync(fixture.UserID, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true);
        Assert.Equal(current, repository.MapToView(entity!).TotpEnabled);
        // Query projection must translate the protected-factor check to SQL.
        await using var verify = fixture.CreateContext();
        var list = await repository.MapToList(verify.Users.Where(x => x.ID == fixture.UserID)).SingleAsync();
        Assert.Equal(current, list.TotpEnabled);
    }
}
