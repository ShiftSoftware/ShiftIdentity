using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
public sealed class PasswordOnlyLegacySqlTests(SqlIdentityFixture fixture)
{
    [Theory]
    [InlineData("password-change")]
    [InlineData("protected-factor")]
    [InlineData("uncopied-factor")]
    [InlineData("recovery")]
    [InlineData("inactive")]
    public async Task Password_only_login_and_refresh_refuse_interactive_or_unavailable_accounts(string condition)
    {
        await fixture.ResetAsync();
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture, c =>
        {
            c.Security.PasswordOnly = true;
            c.Token.ExpireSeconds = int.MaxValue;
            c.RefreshToken.ExpireSeconds = int.MaxValue;
        }, enabled: false);
        string refresh;
        using (var scope = host.Services.CreateScope())
        {
            var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
            var login = await auth.LoginAsync(new LoginDTO { Username = fixture.Username, Password = fixture.Password });
            Assert.Equal(LoginResultEnum.Success, login.Result);
            Assert.Equal(int.MaxValue, login.Token.TokenLifeTimeInSeconds);
            Assert.Equal(int.MaxValue, login.Token.RefreshTokenLifeTimeInSeconds);
            refresh = login.Token.RefreshToken;
        }
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            switch (condition)
            {
                case "password-change": user.RequireChangePassword = true; break;
                case "protected-factor": state.ProtectedTotpSecret = [1, 2, 3]; break;
                case "uncopied-factor": user.TotpSecret = fixture.FactorSecret; break;
                case "recovery": state.LocalMfaRecoveryRequired = true; break;
                case "inactive": user.IsActive = false; break;
            }
            await db.SaveChangesAsync();
        }
        using var verify = host.Services.CreateScope();
        var service = verify.ServiceProvider.GetRequiredService<AuthService>();
        var refused = await service.LoginAsync(new LoginDTO { Username = fixture.Username, Password = fixture.Password });
        Assert.NotEqual(LoginResultEnum.Success, refused.Result);
        Assert.Null(refused.Token);
        Assert.Null(await service.RefreshAsync(refresh));
    }

    [Fact]
    public async Task Removed_factor_uses_current_state_instead_of_retained_plaintext()
    {
        await fixture.ResetAsync();
        await using (var db = fixture.CreateContext())
        {
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
            user.TotpSecret = fixture.FactorSecret;
            var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
            state.FactorGeneration = 2;
            state.ProtectedTotpSecret = null;
            state.LocalMfaRecoveryRequired = false;
            await db.SaveChangesAsync();
        }
        using var host = new ConfiguredIdentityHttpHost<IdentityTestDbContext>(fixture,
            c => { c.Security.PasswordOnly = true; c.MfaSettings.Enabled = false; }, enabled: false);
        using var scope = host.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AuthService>();
        var login = await service.LoginAsync(new LoginDTO { Username = fixture.Username, Password = fixture.Password });
        Assert.Equal(LoginResultEnum.Success, login.Result);
        Assert.NotNull(await service.RefreshAsync(login.Token.RefreshToken));
    }
}
