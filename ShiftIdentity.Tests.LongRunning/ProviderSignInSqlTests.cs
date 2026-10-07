using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Sign in with a provider through the real routes and SQL store, with the provider's token endpoint replaced: the
/// first sign-in matches the account by the verified email and links it, later ones use the link, and the session is
/// a provider session that skips the local steps unless the host asks for Shift MFA. Every case runs for each provider.
/// </summary>
public abstract class ProviderSignInSqlTests(SqlIdentityFixture fixture) : IAsyncLifetime
{
    protected const string Email = "Provider.Person@example.invalid";
    private const string AdminName = "synthetic-provider-admin";
    private const string UsersWrite = "{\"ShiftIdentityActions\":{\"Users\":[\"w\"]}}";
    protected SqlIdentityFixture Fixture { get; } = fixture;
    private protected FakeProvider Tokens { get; } = new();
    private long adminID;

    protected abstract SignInProvider Provider { get; }
    /// <summary>The identity the provider vouches for by default: its email matches the test account's.</summary>
    private protected abstract ProviderIdentity DefaultIdentity { get; }
    /// <summary>Turns this provider on, answering with <see cref="Tokens"/>.</summary>
    protected abstract void Enable(bool requireShiftMfa = false);
    protected abstract void Disable();
    protected string Name => ProviderSignIn.RouteName(Provider);

    public async ValueTask InitializeAsync()
    {
        Fixture.Clock = TimeProvider.System;
        await Fixture.ResetAsync();
        await SetEmailAsync(Email);
        await using var db = Fixture.CreateContext();
        adminID = await db.Users.IgnoreQueryFilters().Where(x => x.Username == AdminName).Select(x => x.ID).SingleOrDefaultAsync();
        if (adminID == 0) adminID = await Fixture.CreateSyntheticUserAsync(AdminName, UsersWrite);
        await db.Set<UserSecurityState>().Where(x => x.UserID == adminID).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 1));
        Enable();
        Tokens.Identity = DefaultIdentity;
    }

    public ValueTask DisposeAsync() { Fixture.Microsoft = null; Fixture.Google = null; return ValueTask.CompletedTask; }

    [Fact]
    public async Task First_sign_in_links_the_account_verifies_its_email_and_issues_a_renewable_provider_session()
    {
        using var host = new IdentityHttpHost(Fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token);
        Assert.Contains(jwt.Claims, x => x.Type == "shift_route" && x.Value == Name);
        Assert.Contains(jwt.Claims, x => x.Type == "shift_mfa" && x.Value == "false");
        var renewed = Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(renewed.Session.Token).Claims, x => x.Type == "shift_route" && x.Value == Name);

        await using var db = Fixture.CreateContext();
        var link = await db.Set<UserProviderLink>().SingleAsync();
        Assert.Equal((Provider, Fixture.UserID, DefaultIdentity.Directory, DefaultIdentity.Subject, Email.ToUpperInvariant(), DefaultIdentity.PersonalAccount),
            (link.Provider, link.UserID, link.TenantID, link.ObjectID, link.EmailLookupKey, link.PersonalAccount));
        var user = await db.Users.SingleAsync(x => x.ID == Fixture.UserID);
        Assert.True(user.EmailVerified);
        var security = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == Fixture.UserID);
        Assert.Equal(RecoveryEmailProvenance.OwnershipVerification, security.RecoveryEmailProvenance);
        Assert.Contains(await db.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(), x => x == "ProviderLinked");
        var notice = Assert.Single(((LocalSecurityInbox)Fixture.EmailSink!).Messages);
        Assert.Equal((Email, "/Identity/login"), (notice.Destination, notice.Link));
        Assert.StartsWith(Provider.ToString() + " sign-in linked", notice.Subject);
    }

    [Fact]
    public async Task A_linked_identity_signs_in_again_without_a_second_notice_even_when_its_provider_email_changed()
    {
        using var host = new IdentityHttpHost(Fixture);
        Assert.IsType<SessionIssued>(await SignInAsync(host));
        Tokens.Identity = Tokens.Identity! with { Email = "renamed@example.invalid" };
        Assert.IsType<SessionIssued>(await SignInAsync(host));
        await using var db = Fixture.CreateContext();
        Assert.Single(await db.Set<UserProviderLink>().ToListAsync());
        Assert.Single(((LocalSecurityInbox)Fixture.EmailSink!).Messages);
    }

    [Fact]
    public async Task No_account_with_the_email_and_an_unverified_email_are_refused_without_writing()
    {
        using var host = new IdentityHttpHost(Fixture);
        Tokens.Identity = Tokens.Identity! with { Email = "nobody@example.invalid" };
        Assert.Equal("ProviderAccountNotFound", await SignInErrorAsync(host));
        Tokens.Identity = Tokens.Identity with { Email = Email, EmailVerified = false };
        Assert.Equal("ProviderEmailUnverified", await SignInErrorAsync(host));
        await using var db = Fixture.CreateContext();
        Assert.Empty(await db.Set<UserProviderLink>().ToListAsync());
        Assert.False((await db.Users.SingleAsync(x => x.ID == Fixture.UserID)).EmailVerified);
    }

    [Fact]
    public async Task Inactive_account_is_refused()
    {
        await using (var db = Fixture.CreateContext())
            await db.Users.Where(x => x.ID == Fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        using var host = new IdentityHttpHost(Fixture);
        Assert.Equal("AccountUnavailable", await SignInErrorAsync(host));
    }

    [Fact]
    public async Task Local_steps_and_password_lockout_do_not_block_a_provider_sign_in_by_default()
    {
        await Fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable();
        await using (var db = Fixture.CreateContext())
            await db.Users.Where(x => x.ID == Fixture.UserID).ExecuteUpdateAsync(x => x
                .SetProperty(u => u.RequireChangePassword, true).SetProperty(u => u.LockDownUntil, DateTime.UtcNow.AddMinutes(10)));
        using var host = new IdentityHttpHost(Fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        // Renewal keeps the provider rule: an enrolled authenticator and a forced change do not restrict it.
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        // A password sign-in still owes both.
        Assert.IsType<AuthenticationRefused>(await host.LoginAsync(Fixture, IdentityHttpHost.Pkce().Challenge));
    }

    [Fact]
    public async Task With_Shift_MFA_required_the_sign_in_continues_into_the_authenticator_step()
    {
        await Fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable(requireShiftMfa: true);
        using var host = new IdentityHttpHost(Fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await SignInAsync(host, pkce)).Challenge;
        Assert.Equal((AuthenticationStep.ExistingMfa, AuthenticationOperationPurpose.Login), (challenge.Step, challenge.Purpose));
        var code = new Totp(Fixture.FactorSecret).ComputeTotp(DateTime.UtcNow);
        var session = Assert.IsType<SessionIssued>(await host.CompleteAsync(challenge.Handle!, code, pkce.Verifier));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token);
        Assert.Contains(jwt.Claims, x => x.Type == "shift_route" && x.Value == Name);
        Assert.Contains(jwt.Claims, x => x.Type == "shift_mfa" && x.Value == "true");
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
    }

    [Fact]
    public async Task With_Shift_MFA_required_on_a_mandatory_host_the_sign_in_enrolls_an_authenticator()
    {
        await Fixture.ChangeMfaPolicyAsync(mandatory: true);
        Enable(requireShiftMfa: true);
        using var host = new IdentityHttpHost(Fixture);
        var pkce = IdentityHttpHost.Pkce();
        var setup = Assert.IsType<ChallengeRequired>(await SignInAsync(host, pkce)).Challenge;
        Assert.Equal((AuthenticationStep.NewMfa, AuthenticationOperationPurpose.MfaEnrollment), (setup.Step, setup.Purpose));
        var code = new Totp(Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret)).ComputeTotp(DateTime.UtcNow);
        var session = Assert.IsType<SessionIssued>(Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(setup.Handle!, code, pkce.Verifier)).Continuation);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token).Claims, x => x.Type == "shift_route" && x.Value == Name);
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
    }

    [Fact]
    public async Task An_app_code_carries_the_provider_session_to_the_app_and_its_renewal()
    {
        await Fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable();
        await using (var db = Fixture.CreateContext())
        {
            foreach (var row in await db.Apps.IgnoreQueryFilters().ToListAsync())
            {
                row.IsDeleted = false; row.AppSecret = null;
                row.RedirectUri = row.AppId == "other-client" ? "https://other.invalid/callback" : "https://client.invalid/callback";
            }
            await db.SaveChangesAsync();
        }
        SessionIssued session;
        using (var host = new IdentityHttpHost(Fixture)) session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        using var legacy = new LegacyIdentityHttpHost<IdentityTestDbContext>(Fixture, authority: true);
        var verifier = Guid.NewGuid().ToString();
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/Auth/AuthCode")
        {
            Content = JsonContent.Create(new ShiftSoftware.ShiftIdentity.Core.DTOs.Auth.GenerateAuthCodeDTO
                { AppId = "other-client", ReturnUrl = "/", CodeChallenge = ShiftSoftware.ShiftIdentity.Core.HashService.SHA512GenerateHash(verifier) })
        };
        create.Headers.Authorization = new("Bearer", session.Session.Token);
        using var created = await legacy.Client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var code = (await created.Content.ReadFromJsonAsync<ShiftSoftware.ShiftEntity.Model.ShiftEntityResponse<ShiftSoftware.ShiftIdentity.Core.Models.AuthCodeModel>>())!.Entity!;
        using var exchanged = await legacy.Client.PostAsJsonAsync("/api/Auth/TokenWithAppIdOnly",
            new ShiftSoftware.ShiftIdentity.Core.DTOs.GenerateExternalTokenWithAppIdOnlyDTO { AppId = "other-client", AuthCode = code.Code, CodeVerifier = verifier });
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        var app = (await exchanged.Content.ReadFromJsonAsync<ShiftSoftware.ShiftEntity.Model.ShiftEntityResponse<ShiftSoftware.ShiftIdentity.Core.DTOs.TokenDTO>>())!.Entity!;
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(app.Token).Claims, x => x.Type == "shift_route" && x.Value == Name);
        // An enrolled authenticator does not restrict the app session's renewal, as it does not restrict the provider session's.
        using var renewed = await legacy.Client.PostAsJsonAsync("/api/Auth/Refresh", new { app.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
    }

    [Fact]
    public async Task The_account_and_an_operator_who_can_read_users_see_the_link_and_others_do_not()
    {
        using var host = new IdentityHttpHost(Fixture);
        var own = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var listed = Assert.Single(Assert.IsType<ProviderLinksRead>(await ReadLinksAsync(host, own.Session.Token, null)).Links);
        Assert.Equal((Provider, DefaultIdentity.Email, DefaultIdentity.PersonalAccount), (listed.Provider, listed.Email, listed.PersonalAccount));

        var key = new ShiftSoftware.ShiftEntity.Core.HashIdService(Microsoft.Extensions.Options.Options.Create(new ShiftSoftware.ShiftEntity.Core.ShiftEntityOptions()))
            .Encode<ShiftSoftware.ShiftIdentity.Core.DTOs.User.UserDTO>(Fixture.UserID);
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, Fixture.Password, IdentityHttpHost.Pkce().Challenge))));
        await using (var db = Fixture.CreateContext())
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"]}}"));
        Assert.Single(Assert.IsType<ProviderLinksRead>(await ReadLinksAsync(host, admin.Session.Token, key)).Links);
        await using (var db = Fixture.CreateContext())
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, "{}"));
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await ReadLinksAsync(host, admin.Session.Token, key)).Code);
        await using (var db = Fixture.CreateContext())
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, UsersWrite));
    }

    [Fact]
    public async Task The_completion_handle_needs_the_starting_verifier_and_is_single_use()
    {
        using var host = new IdentityHttpHost(Fixture);
        var pkce = IdentityHttpHost.Pkce();
        var handle = await CallbackHandleAsync(host, pkce.Challenge);
        Assert.Equal(AuthenticationFailure.InvalidGrant,
            Assert.IsType<AuthenticationRefused>(await CompleteAsync(host, handle, IdentityHttpHost.Pkce().Verifier)).Code);
        Assert.IsType<SessionIssued>(await CompleteAsync(host, handle, pkce.Verifier));
        Assert.IsType<AuthenticationRefused>(await CompleteAsync(host, handle, pkce.Verifier));
    }

    [Fact]
    public async Task An_email_change_ends_the_session_and_the_link_no_longer_reaches_the_account()
    {
        using var host = new IdentityHttpHost(Fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, Fixture.Password, IdentityHttpHost.Pkce().Challenge))));
        Assert.IsType<AdminAccountChanged>(await host.AdminChangeEmailAsync(admin.Session.Token, Fixture.UserID, "corrected@example.invalid", false));
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(session.Session.RefreshToken)).Code);
        Assert.Equal("ProviderAccountNotFound", await SignInErrorAsync(host));
    }

    [Fact]
    public async Task A_provider_session_does_not_count_as_recent_authentication_for_administrator_changes()
    {
        await SetEmailAsync(null);
        await SetEmailAsync("provider.admin@example.invalid", adminID);
        Tokens.Identity = Tokens.Identity! with { Email = "provider.admin@example.invalid" };
        using var host = new IdentityHttpHost(Fixture);
        var admin = Assert.IsType<SessionIssued>(await SignInAsync(host));
        Assert.Equal(AuthenticationFailure.ReauthenticationRequired, Assert.IsType<AuthenticationRefused>(
            await host.AdminSetActiveAsync(admin.Session.Token, Fixture.UserID, false)).Code);
    }

    [Fact]
    public async Task Turning_the_provider_off_refuses_new_sign_ins_and_ends_provider_sessions()
    {
        SessionIssued session;
        using (var host = new IdentityHttpHost(Fixture)) session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        Disable();
        using var off = new IdentityHttpHost(Fixture);
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await StartAsync(off, IdentityHttpHost.Pkce().Challenge)).Code);
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await off.RefreshAsync(session.Session.RefreshToken)).Code);
        Assert.DoesNotContain(Provider, (await off.Client.GetFromJsonAsync<SignInProviders>("/api/identity/v2/providers"))!.Enabled);
    }

    [Fact]
    public async Task A_tampered_state_is_refused_before_the_provider_is_asked()
    {
        using var host = new IdentityHttpHost(Fixture);
        var redirect = Assert.IsType<ProviderRedirect>(await StartAsync(host, IdentityHttpHost.Pkce().Challenge));
        var state = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query)["state"].ToString();
        var tampered = state[..^2] + (state[^2] == 'A' ? "B" : "A") + state[^1];
        using var response = await host.Client.GetAsync($"/api/identity/v2/providers/{Name}/callback?code=c&state={Uri.EscapeDataString(tampered)}");
        Assert.EndsWith($"#{Name}-error=InvalidGrant", response.Headers.Location!.ToString());
        Assert.Equal(0, Tokens.Calls);
    }

    protected async Task SetEmailAsync(string? email, long? userID = null)
    {
        var id = userID ?? Fixture.UserID;
        await using var db = Fixture.CreateContext();
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == id).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.Email, email).SetProperty(u => u.EmailVerified, false));
        await db.Set<UserSecurityState>().Where(x => x.UserID == id).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.EmailLookupKey, email == null ? null : RecoveryContact.Key(email)));
    }

    protected static async Task<AuthOutcome> ReadLinksAsync(IdentityHttpHost host, string access, string? userKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/v2/providers/links" + (userKey is null ? "" : "/" + userKey));
        request.Headers.Authorization = new("Bearer", access);
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }

    protected Task<AuthOutcome> StartAsync(IdentityHttpHost host, string challenge) => StartAsync(host, Name, challenge);

    protected static async Task<AuthOutcome> StartAsync(IdentityHttpHost host, string name, string challenge) => await IdentityHttpHost.Read(
        await host.Client.PostAsJsonAsync($"/api/identity/v2/providers/{name}/start", new StartProviderSignInRequest(challenge)));

    private protected async Task<string> CallbackLocationAsync(IdentityHttpHost host, string challenge, FakeProvider? tokens = null, string? name = null)
    {
        tokens ??= Tokens;
        name ??= Name;
        var redirect = Assert.IsType<ProviderRedirect>(await StartAsync(host, name, challenge));
        var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
        Assert.Equal(("S256", "select_account", "code"), (query["code_challenge_method"].ToString(), query["prompt"].ToString(), query["response_type"].ToString()));
        tokens.Expect(query["nonce"]!, query["code_challenge"]!, query["redirect_uri"]!);
        using var response = await host.Client.GetAsync($"/api/identity/v2/providers/{name}/callback?code=synthetic-code&state=" +
            Uri.EscapeDataString(query["state"]!));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://identity.example.invalid/Identity/login#", location);
        return location;
    }

    private protected async Task<string> CallbackHandleAsync(IdentityHttpHost host, string challenge, FakeProvider? tokens = null, string? name = null)
    {
        var location = await CallbackLocationAsync(host, challenge, tokens, name);
        var marker = $"#{name ?? Name}=";
        Assert.Contains(marker, location);
        return Uri.UnescapeDataString(location[(location.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
    }

    protected async Task<string> SignInErrorAsync(IdentityHttpHost host)
    {
        var location = await CallbackLocationAsync(host, IdentityHttpHost.Pkce().Challenge);
        var marker = $"#{Name}-error=";
        Assert.Contains(marker, location);
        return location[(location.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
    }

    private protected async Task<AuthOutcome> SignInAsync(IdentityHttpHost host, (string Verifier, string Challenge)? pkce = null,
        FakeProvider? tokens = null, string? name = null)
    {
        var proof = pkce ?? IdentityHttpHost.Pkce();
        return await CompleteAsync(host, await CallbackHandleAsync(host, proof.Challenge, tokens, name), proof.Verifier, name);
    }

    protected async Task<AuthOutcome> CompleteAsync(IdentityHttpHost host, string handle, string verifier, string? name = null) => await IdentityHttpHost.Read(
        await host.Client.PostAsJsonAsync($"/api/identity/v2/providers/{name ?? Name}/complete", new CompleteProviderSignInRequest(handle, verifier)));

    /// <summary>Stands in for the provider's token endpoint, checking that the redemption carries what the start sent.</summary>
    internal sealed class FakeProvider : IProviderTokenClient
    {
        private string? nonce, challenge, redirectUri;
        public ProviderIdentity? Identity { get; set; }
        public int Calls { get; private set; }
        public void Expect(string nonce, string challenge, string redirectUri) => (this.nonce, this.challenge, this.redirectUri) = (nonce, challenge, redirectUri);

        public Task<ProviderIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, string nonce, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("synthetic-code", code);
            Assert.Equal(this.nonce, nonce);
            Assert.Equal(this.redirectUri, redirectUri);
            Assert.Equal(challenge, WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(codeVerifier))));
            return Task.FromResult(Identity);
        }
    }
}
