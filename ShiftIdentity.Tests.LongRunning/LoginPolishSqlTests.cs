using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL"), Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class LoginPolishSqlTests(SqlIdentityFixture fixture)
{
    [Theory]
    [InlineData("legacy")]
    [InlineData("adapter")]
    [InlineData("staged")]
    public async Task Login_trims_only_outer_username_whitespace_and_preserves_password(string route)
    {
        await fixture.ResetAsync();
        const string username = "Synthetic  User", password = "  Synthetic password 39!\t";
        try
        {
            await using (var db = fixture.CreateContext())
            {
                var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID, TestContext.Current.CancellationToken);
                var hash = HashService.GenerateVersionedHash(password);
                user.Username = username; user.PasswordHash = hash.PasswordHash; user.Salt = hash.Salt;
                await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            }
            using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: route != "legacy");
            Assert.True(await Accepted(host.Client, route, " \t" + username + "\r\n", password));
            Assert.False(await Accepted(host.Client, route, username, password.Trim()));
            Assert.False(await Accepted(host.Client, route, username.Replace("  ", " "), password));
            await using var verify = fixture.CreateContext();
            Assert.Equal(username, (await verify.Users.SingleAsync(x => x.ID == fixture.UserID, TestContext.Current.CancellationToken)).Username);
        }
        finally
        {
            // The collection shares this account. ResetAsync deliberately does not reset its username.
            await using var db = fixture.CreateContext();
            var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID, CancellationToken.None);
            user.Username = fixture.Username; user.LoginAttempts = 0;
            await db.SaveChangesAsync(CancellationToken.None);
            await fixture.ResetAsync();
        }
    }

    [Theory]
    [InlineData("legacy", "")]
    [InlineData("legacy", " \t\r\n")]
    [InlineData("adapter", "")]
    [InlineData("adapter", " \t\r\n")]
    [InlineData("staged", "")]
    [InlineData("staged", " \t\r\n")]
    public async Task Empty_username_is_refused_without_admitting_or_changing_an_account(string route, string username)
    {
        await fixture.ResetAsync();
        using var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: route != "legacy");
        Assert.False(await Accepted(host.Client, route, username, fixture.Password));
        await using var db = fixture.CreateContext();
        Assert.Equal(0, (await db.Set<UserSecurityState>().SingleAsync(TestContext.Current.CancellationToken)).FailedProofs);
        Assert.Empty(await db.Set<AuthenticationOperation>().ToListAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<bool> Accepted(HttpClient client, string route, string username, string password)
    {
        if (route == "staged")
        {
            using var response = await client.PostAsJsonAsync("api/identity/v2/login",
                new PasswordLoginRequest(username, password, IdentityHttpHost.Pkce().Challenge), TestContext.Current.CancellationToken);
            var result = await response.Content.ReadFromJsonAsync<AuthOutcome>(TestContext.Current.CancellationToken);
            Assert.True(result is SessionIssued or AuthenticationRefused);
            return result is SessionIssued;
        }
        using var deployed = await client.PostAsJsonAsync("api/Auth/Login", new LoginDTO { Username = username, Password = password }, TestContext.Current.CancellationToken);
        var envelope = await deployed.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>(TestContext.Current.CancellationToken);
        Assert.Equal(deployed.IsSuccessStatusCode, envelope?.Entity is not null);
        return deployed.IsSuccessStatusCode;
    }
}
