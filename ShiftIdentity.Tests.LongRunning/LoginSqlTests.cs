using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

[Collection("Identity SQL")]
[Trait("Category", "Sql")]
[Trait("Category", "Http")]
public sealed class LoginSqlTests(SqlIdentityFixture fixture)
{
    [Fact]
    public async Task Password_login_issues_bound_credentials_and_refresh_preserves_context()
    {
        await fixture.ResetAsync();
        using var host = new IdentityHttpHost(fixture, new("test-client", "test-api", true));
        var login = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        var renewed = Assert.IsType<SessionIssued>(await host.RefreshAsync(login.Session.RefreshToken));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(renewed.Session.Token);
        Assert.Equal("test-api", Assert.Single(jwt.Audiences));
        Assert.Contains(jwt.Claims, x => x.Type == "shift_external" && x.Value == "true");
        Assert.Contains(jwt.Claims, x => x.Type == "shift_sv" && x.Value == "1");
        Assert.Contains(jwt.Claims, x => x.Type == "shift_client" && x.Value == "test-client");
        Assert.InRange(jwt.ValidTo - jwt.ValidFrom, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task Mfa_completion_is_single_use_and_persists_the_accepted_time_step()
    {
        await fixture.ResetAsync(true);
        using var first = new IdentityHttpHost(fixture);
        using var second = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await first.LoginAsync(fixture, pkce.Challenge)).Challenge;
        var code = new Totp(fixture.FactorSecret).ComputeTotp(fixture.Clock.GetUtcNow().UtcDateTime);
        var results = await Task.WhenAll(first.CompleteAsync(challenge.Handle!, code, pkce.Verifier),
            second.CompleteAsync(challenge.Handle!, code, pkce.Verifier));
        Assert.Single(results.OfType<SessionIssued>());
        Assert.Single(results.OfType<AuthenticationRefused>());
        await using var db = fixture.CreateContext();
        Assert.NotNull((await db.Set<UserSecurityState>().SingleAsync()).LastAcceptedTotpStep);
        Assert.Equal(AuthenticationOperationState.Completed, (await db.Set<AuthenticationOperation>().SingleAsync()).State);
    }

    [Fact]
    public async Task Incorrect_password_is_committed_without_creating_an_operation()
    {
        await fixture.ResetAsync();
        using var host = new IdentityHttpHost(fixture);
        Assert.Equal(AuthenticationFailure.InvalidProof,
            Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, "wrong")).Code);
        await using var db = fixture.CreateContext();
        Assert.Equal(1, (await db.Set<UserSecurityState>().SingleAsync()).FailedProofs);
        Assert.Empty(await db.Set<AuthenticationOperation>().ToListAsync());
    }
}
