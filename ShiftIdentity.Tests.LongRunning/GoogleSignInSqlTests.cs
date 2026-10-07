using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Sign in with Google through the real routes and SQL store: every case of <see cref="ProviderSignInSqlTests"/>, and
/// Google beside Microsoft on one host, where each provider keeps its own state, settings and sessions.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class GoogleSignInSqlTests(SqlIdentityFixture fixture) : ProviderSignInSqlTests(fixture), IClassFixture<SqlIdentityFixture>
{
    private static readonly string Subject = string.Concat(Enumerable.Range(0, 21).Select(_ => (char)('0' + Random.Shared.Next(10))));
    private readonly FakeProvider microsoft = new()
    {
        Identity = new(Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D"), Email, true, true)
    };

    protected override SignInProvider Provider => SignInProvider.Google;
    // A personal Google account on an address Google does not host: Google checked it once, and that is enough (owner, 7 October 2026).
    private protected override ProviderIdentity DefaultIdentity => new(GoogleSignIn.Issuer, Subject, "provider.person@EXAMPLE.invalid", true, true);

    protected override void Enable(bool requireShiftMfa = false) => Fixture.Google = new GoogleSignIn(
        new GoogleSignInSettings { Enabled = true, ClientId = "test.apps.googleusercontent.com", ClientSecret = "test", RequireShiftMfa = requireShiftMfa },
        "https://identity.example.invalid", Tokens);

    protected override void Disable() => Fixture.Google = null;

    private void EnableMicrosoft(bool requireShiftMfa = false) => Fixture.Microsoft = new MicrosoftSignIn(
        new MicrosoftSignInSettings { Enabled = true, ClientId = Guid.NewGuid().ToString(), ClientSecret = "test", RequireShiftMfa = requireShiftMfa },
        "https://identity.example.invalid", microsoft);

    [Fact]
    public async Task A_Google_sign_in_state_is_refused_at_the_Microsoft_callback()
    {
        EnableMicrosoft();
        using var host = new IdentityHttpHost(Fixture);
        var redirect = Assert.IsType<ProviderRedirect>(await StartAsync(host, IdentityHttpHost.Pkce().Challenge));
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", redirect.Url);
        var state = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query)["state"].ToString();
        using var response = await host.Client.GetAsync($"/api/identity/v2/providers/microsoft/callback?code=synthetic-code&state={Uri.EscapeDataString(state)}");
        Assert.EndsWith("#microsoft-error=InvalidGrant", response.Headers.Location!.ToString());
        Assert.Equal((0, 0), (microsoft.Calls, Tokens.Calls));
    }

    [Fact]
    public async Task Each_provider_follows_its_own_Shift_MFA_setting()
    {
        await Fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable(requireShiftMfa: false);
        EnableMicrosoft(requireShiftMfa: true);
        using var host = new IdentityHttpHost(Fixture);
        var google = Assert.IsType<SessionIssued>(await SignInAsync(host));
        Assert.IsType<SessionIssued>(await host.RefreshAsync(google.Session.RefreshToken));
        var viaMicrosoft = Assert.IsType<ChallengeRequired>(await SignInAsync(host, tokens: microsoft, name: "microsoft")).Challenge;
        Assert.Equal(AuthenticationStep.ExistingMfa, viaMicrosoft.Step);
    }

    [Fact]
    public async Task One_account_links_a_Microsoft_and_a_Google_identity_and_turning_Google_off_leaves_Microsoft_sessions()
    {
        EnableMicrosoft();
        SessionIssued viaGoogle, viaMicrosoft;
        using (var host = new IdentityHttpHost(Fixture))
        {
            viaGoogle = Assert.IsType<SessionIssued>(await SignInAsync(host));
            viaMicrosoft = Assert.IsType<SessionIssued>(await SignInAsync(host, tokens: microsoft, name: "microsoft"));
            Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(viaMicrosoft.Session.Token).Claims, x => x.Type == "shift_route" && x.Value == "microsoft");
            var links = Assert.IsType<ProviderLinksRead>(await ReadLinksAsync(host, viaGoogle.Session.Token, null)).Links;
            Assert.Equal([SignInProvider.Microsoft, SignInProvider.Google], links.Select(x => x.Provider).Order());
            Assert.Equal(new SignInProviders(true, true), await host.Client.GetFromJsonAsync<SignInProviders>("/api/identity/v2/providers"));
        }
        Disable();
        using var off = new IdentityHttpHost(Fixture);
        Assert.IsType<SessionIssued>(await off.RefreshAsync(viaMicrosoft.Session.RefreshToken));
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await off.RefreshAsync(viaGoogle.Session.RefreshToken)).Code);
        // Both links stay recorded; only the host's switch decides whether Google can sign in.
        await using var db = Fixture.CreateContext();
        Assert.Equal(2, await db.Set<UserProviderLink>().CountAsync());
    }

    [Fact]
    public async Task A_Workspace_identity_is_recorded_as_a_work_account_and_a_long_subject_fits()
    {
        Tokens.Identity = DefaultIdentity with { Subject = new string('7', 255), PersonalAccount = false };
        using var host = new IdentityHttpHost(Fixture);
        Assert.IsType<SessionIssued>(await SignInAsync(host));
        await using var db = Fixture.CreateContext();
        var link = await db.Set<UserProviderLink>().SingleAsync();
        Assert.Equal((SignInProvider.Google, GoogleSignIn.Issuer, 255, false), (link.Provider, link.TenantID, link.ObjectID.Length, link.PersonalAccount));
    }

    [Fact]
    public async Task The_schema_refuses_an_unknown_provider()
    {
        await using var db = Fixture.CreateContext();
        db.Set<UserProviderLink>().Add(new UserProviderLink
        {
            ID = Guid.NewGuid(), UserID = Fixture.UserID, Provider = (SignInProvider)3, TenantID = "t", ObjectID = "o",
            EmailLookupKey = "K", Email = "k@example.invalid", CreatedAt = DateTimeOffset.UtcNow, LastUsedAt = DateTimeOffset.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
