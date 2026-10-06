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
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// Sign in with Microsoft through the real routes and SQL store, with Microsoft's token endpoint replaced: the first
/// sign-in matches the account by the verified email and links it, later ones use the link, and the session is a
/// provider session that skips the local steps unless the host asks for Shift MFA.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class MicrosoftSignInSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string Email = "Microsoft.Person@example.invalid";
    private const string AdminName = "synthetic-microsoft-admin";
    private const string UsersWrite = "{\"ShiftIdentityActions\":{\"Users\":[\"w\"]}}";
    private static readonly string Tenant = Guid.NewGuid().ToString("D");
    private static readonly string Person = Guid.NewGuid().ToString("D");
    private readonly FakeMicrosoft microsoft = new();
    private long adminID;

    public async ValueTask InitializeAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync();
        await SetEmailAsync(Email);
        await using var db = fixture.CreateContext();
        adminID = await db.Users.IgnoreQueryFilters().Where(x => x.Username == AdminName).Select(x => x.ID).SingleOrDefaultAsync();
        if (adminID == 0) adminID = await fixture.CreateSyntheticUserAsync(AdminName, UsersWrite);
        await db.Set<UserSecurityState>().Where(x => x.UserID == adminID).ExecuteUpdateAsync(x => x.SetProperty(s => s.SecurityVersion, 1));
        Enable();
        microsoft.Identity = new(Tenant, Person, "microsoft.person@EXAMPLE.invalid", true);
    }

    public ValueTask DisposeAsync() { fixture.Microsoft = null; return ValueTask.CompletedTask; }

    private void Enable(bool requireShiftMfa = false) => fixture.Microsoft = new MicrosoftSignIn(
        new MicrosoftSignInSettings { Enabled = true, ClientId = Guid.NewGuid().ToString(), ClientSecret = "test", RequireShiftMfa = requireShiftMfa },
        "https://identity.example.invalid", microsoft);

    [Fact]
    public async Task First_sign_in_links_the_account_verifies_its_email_and_issues_a_renewable_provider_session()
    {
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token);
        Assert.Contains(jwt.Claims, x => x.Type == "shift_route" && x.Value == "microsoft");
        Assert.Contains(jwt.Claims, x => x.Type == "shift_mfa" && x.Value == "false");
        var renewed = Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(renewed.Session.Token).Claims, x => x.Type == "shift_route" && x.Value == "microsoft");

        await using var db = fixture.CreateContext();
        var link = await db.Set<UserProviderLink>().SingleAsync();
        Assert.Equal((fixture.UserID, Tenant, Person, Email.ToUpperInvariant()), (link.UserID, link.TenantID, link.ObjectID, link.EmailLookupKey));
        var user = await db.Users.SingleAsync(x => x.ID == fixture.UserID);
        Assert.True(user.EmailVerified);
        var security = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == fixture.UserID);
        Assert.Equal(RecoveryEmailProvenance.OwnershipVerification, security.RecoveryEmailProvenance);
        Assert.Contains(await db.Set<AuthenticationAuditEvent>().Select(x => x.Outcome).ToListAsync(), x => x == "ProviderLinked");
        var notice = Assert.Single(((LocalSecurityInbox)fixture.EmailSink!).Messages);
        Assert.Equal((Email, "/Identity/login"), (notice.Destination, notice.Link));
    }

    [Fact]
    public async Task A_linked_identity_signs_in_again_without_a_second_notice_even_when_its_Microsoft_email_changed()
    {
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SessionIssued>(await SignInAsync(host));
        microsoft.Identity = microsoft.Identity! with { Email = "renamed@example.invalid" };
        Assert.IsType<SessionIssued>(await SignInAsync(host));
        await using var db = fixture.CreateContext();
        Assert.Single(await db.Set<UserProviderLink>().ToListAsync());
        Assert.Single(((LocalSecurityInbox)fixture.EmailSink!).Messages);
    }

    [Fact]
    public async Task No_account_with_the_email_and_an_unverified_email_are_refused_without_writing()
    {
        using var host = new IdentityHttpHost(fixture);
        microsoft.Identity = microsoft.Identity! with { Email = "nobody@example.invalid" };
        Assert.Equal("ProviderAccountNotFound", await SignInErrorAsync(host));
        microsoft.Identity = microsoft.Identity with { Email = Email, EmailVerified = false };
        Assert.Equal("ProviderEmailUnverified", await SignInErrorAsync(host));
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Set<UserProviderLink>().ToListAsync());
        Assert.False((await db.Users.SingleAsync(x => x.ID == fixture.UserID)).EmailVerified);
    }

    [Fact]
    public async Task Inactive_account_is_refused()
    {
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsActive, false));
        using var host = new IdentityHttpHost(fixture);
        Assert.Equal("AccountUnavailable", await SignInErrorAsync(host));
    }

    [Fact]
    public async Task Local_steps_and_password_lockout_do_not_block_a_Microsoft_sign_in_by_default()
    {
        await fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable();
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x
                .SetProperty(u => u.RequireChangePassword, true).SetProperty(u => u.LockDownUntil, DateTime.UtcNow.AddMinutes(10)));
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        // Renewal keeps the provider rule: an enrolled authenticator and a forced change do not restrict it.
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        // A password sign-in still owes both.
        Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
    }

    [Fact]
    public async Task With_Shift_MFA_required_the_sign_in_continues_into_the_authenticator_step()
    {
        await fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable(requireShiftMfa: true);
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var challenge = Assert.IsType<ChallengeRequired>(await SignInAsync(host, pkce)).Challenge;
        Assert.Equal((AuthenticationStep.ExistingMfa, AuthenticationOperationPurpose.Login), (challenge.Step, challenge.Purpose));
        var code = new Totp(fixture.FactorSecret).ComputeTotp(DateTime.UtcNow);
        var session = Assert.IsType<SessionIssued>(await host.CompleteAsync(challenge.Handle!, code, pkce.Verifier));
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token);
        Assert.Contains(jwt.Claims, x => x.Type == "shift_route" && x.Value == "microsoft");
        Assert.Contains(jwt.Claims, x => x.Type == "shift_mfa" && x.Value == "true");
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
    }

    [Fact]
    public async Task With_Shift_MFA_required_on_a_mandatory_host_the_sign_in_enrolls_an_authenticator()
    {
        await fixture.ChangeMfaPolicyAsync(mandatory: true);
        Enable(requireShiftMfa: true);
        using var host = new IdentityHttpHost(fixture);
        var pkce = IdentityHttpHost.Pkce();
        var setup = Assert.IsType<ChallengeRequired>(await SignInAsync(host, pkce)).Challenge;
        Assert.Equal((AuthenticationStep.NewMfa, AuthenticationOperationPurpose.MfaEnrollment), (setup.Step, setup.Purpose));
        var code = new Totp(Base32Encoding.ToBytes(setup.NewAuthenticator!.Secret)).ComputeTotp(DateTime.UtcNow);
        var session = Assert.IsType<SessionIssued>(Assert.IsType<MfaChanged>(await host.ConfirmFactorAsync(setup.Handle!, code, pkce.Verifier)).Continuation);
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token).Claims, x => x.Type == "shift_route" && x.Value == "microsoft");
        Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
    }

    [Fact]
    public async Task An_app_code_carries_the_provider_session_to_the_app_and_its_renewal()
    {
        await fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Enable();
        await using (var db = fixture.CreateContext())
        {
            foreach (var row in await db.Apps.IgnoreQueryFilters().ToListAsync())
            {
                row.IsDeleted = false; row.AppSecret = null;
                row.RedirectUri = row.AppId == "other-client" ? "https://other.invalid/callback" : "https://client.invalid/callback";
            }
            await db.SaveChangesAsync();
        }
        SessionIssued session;
        using (var host = new IdentityHttpHost(fixture)) session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        using var legacy = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture, authority: true);
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
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(app.Token).Claims, x => x.Type == "shift_route" && x.Value == "microsoft");
        // An enrolled authenticator does not restrict the app session's renewal, as it does not restrict the provider session's.
        using var renewed = await legacy.Client.PostAsJsonAsync("/api/Auth/Refresh", new { app.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, renewed.StatusCode);
    }

    [Fact]
    public async Task The_account_and_an_operator_who_can_read_users_see_the_link_and_others_do_not()
    {
        using var host = new IdentityHttpHost(fixture);
        var own = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var listed = Assert.Single(Assert.IsType<ProviderLinksRead>(await ReadLinksAsync(host, own.Session.Token, null)).Links);
        Assert.Equal((SignInProvider.Microsoft, "microsoft.person@EXAMPLE.invalid", false), (listed.Provider, listed.Email, listed.PersonalAccount));

        var key = new ShiftSoftware.ShiftEntity.Core.HashIdService(Microsoft.Extensions.Options.Options.Create(new ShiftSoftware.ShiftEntity.Core.ShiftEntityOptions()))
            .Encode<ShiftSoftware.ShiftIdentity.Core.DTOs.User.UserDTO>(fixture.UserID);
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge))));
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, "{\"ShiftIdentityActions\":{\"Users\":[\"r\",\"w\"]}}"));
        Assert.Single(Assert.IsType<ProviderLinksRead>(await ReadLinksAsync(host, admin.Session.Token, key)).Links);
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, "{}"));
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await ReadLinksAsync(host, admin.Session.Token, key)).Code);
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == adminID).ExecuteUpdateAsync(x => x.SetProperty(u => u.AccessTree, UsersWrite));
    }

    [Fact]
    public async Task The_completion_handle_needs_the_starting_verifier_and_is_single_use()
    {
        using var host = new IdentityHttpHost(fixture);
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
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var admin = Assert.IsType<SessionIssued>(await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync("/api/identity/v2/login",
            new PasswordLoginRequest(AdminName, fixture.Password, IdentityHttpHost.Pkce().Challenge))));
        Assert.IsType<AdminAccountChanged>(await host.AdminChangeEmailAsync(admin.Session.Token, fixture.UserID, "corrected@example.invalid", false));
        Assert.Equal(AuthenticationFailure.StaleOperation, Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(session.Session.RefreshToken)).Code);
        Assert.Equal("ProviderAccountNotFound", await SignInErrorAsync(host));
    }

    [Fact]
    public async Task A_provider_session_does_not_count_as_recent_authentication_for_administrator_changes()
    {
        await SetEmailAsync(null);
        await SetEmailAsync("microsoft.admin@example.invalid", adminID);
        microsoft.Identity = microsoft.Identity! with { Email = "microsoft.admin@example.invalid" };
        using var host = new IdentityHttpHost(fixture);
        var admin = Assert.IsType<SessionIssued>(await SignInAsync(host));
        Assert.Equal(AuthenticationFailure.ReauthenticationRequired, Assert.IsType<AuthenticationRefused>(
            await host.AdminSetActiveAsync(admin.Session.Token, fixture.UserID, false)).Code);
    }

    [Fact]
    public async Task Turning_Microsoft_off_refuses_new_sign_ins_and_ends_provider_sessions()
    {
        SessionIssued session;
        using (var host = new IdentityHttpHost(fixture)) session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        fixture.Microsoft = null;
        using var off = new IdentityHttpHost(fixture);
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await StartAsync(off, IdentityHttpHost.Pkce().Challenge)).Code);
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await off.RefreshAsync(session.Session.RefreshToken)).Code);
        Assert.False((await off.Client.GetFromJsonAsync<SignInProviders>("/api/identity/v2/providers"))!.Microsoft);
    }

    [Fact]
    public async Task A_tampered_state_is_refused_before_Microsoft_is_asked()
    {
        using var host = new IdentityHttpHost(fixture);
        var redirect = Assert.IsType<ProviderRedirect>(await StartAsync(host, IdentityHttpHost.Pkce().Challenge));
        var state = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query)["state"].ToString();
        var tampered = state[..^2] + (state[^2] == 'A' ? "B" : "A") + state[^1];
        using var response = await host.Client.GetAsync($"/api/identity/v2/providers/microsoft/callback?code=c&state={Uri.EscapeDataString(tampered)}");
        Assert.EndsWith("#microsoft-error=InvalidGrant", response.Headers.Location!.ToString());
        Assert.Equal(0, microsoft.Calls);
    }

    private async Task SetEmailAsync(string? email, long? userID = null)
    {
        var id = userID ?? fixture.UserID;
        await using var db = fixture.CreateContext();
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == id).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.Email, email).SetProperty(u => u.EmailVerified, false));
        await db.Set<UserSecurityState>().Where(x => x.UserID == id).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.EmailLookupKey, email == null ? null : RecoveryContact.Key(email)));
    }

    private static async Task<AuthOutcome> ReadLinksAsync(IdentityHttpHost host, string access, string? userKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/identity/v2/providers/links" + (userKey is null ? "" : "/" + userKey));
        request.Headers.Authorization = new("Bearer", access);
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }

    private static async Task<AuthOutcome> StartAsync(IdentityHttpHost host, string challenge) => await IdentityHttpHost.Read(
        await host.Client.PostAsJsonAsync("/api/identity/v2/providers/microsoft/start", new StartProviderSignInRequest(challenge)));

    private async Task<string> CallbackLocationAsync(IdentityHttpHost host, string challenge)
    {
        var redirect = Assert.IsType<ProviderRedirect>(await StartAsync(host, challenge));
        var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
        Assert.Equal(("S256", "select_account", "code"), (query["code_challenge_method"].ToString(), query["prompt"].ToString(), query["response_type"].ToString()));
        microsoft.Expect(query["nonce"]!, query["code_challenge"]!, query["redirect_uri"]!);
        using var response = await host.Client.GetAsync("/api/identity/v2/providers/microsoft/callback?code=synthetic-code&state=" +
            Uri.EscapeDataString(query["state"]!));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.StartsWith("https://identity.example.invalid/Identity/login#", location);
        return location;
    }

    private async Task<string> CallbackHandleAsync(IdentityHttpHost host, string challenge)
    {
        var location = await CallbackLocationAsync(host, challenge);
        var marker = "#microsoft=";
        Assert.Contains(marker, location);
        return Uri.UnescapeDataString(location[(location.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
    }

    private async Task<string> SignInErrorAsync(IdentityHttpHost host)
    {
        var location = await CallbackLocationAsync(host, IdentityHttpHost.Pkce().Challenge);
        var marker = "#microsoft-error=";
        Assert.Contains(marker, location);
        return location[(location.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..];
    }

    private async Task<AuthOutcome> SignInAsync(IdentityHttpHost host, (string Verifier, string Challenge)? pkce = null)
    {
        var proof = pkce ?? IdentityHttpHost.Pkce();
        return await CompleteAsync(host, await CallbackHandleAsync(host, proof.Challenge), proof.Verifier);
    }

    private static async Task<AuthOutcome> CompleteAsync(IdentityHttpHost host, string handle, string verifier) => await IdentityHttpHost.Read(
        await host.Client.PostAsJsonAsync("/api/identity/v2/providers/microsoft/complete", new CompleteProviderSignInRequest(handle, verifier)));

    /// <summary>Stands in for Microsoft's token endpoint, checking that the redemption carries what the start sent.</summary>
    private sealed class FakeMicrosoft : IMicrosoftTokenClient
    {
        private string? nonce, challenge, redirectUri;
        public MicrosoftIdentity? Identity { get; set; }
        public int Calls { get; private set; }
        public void Expect(string nonce, string challenge, string redirectUri) => (this.nonce, this.challenge, this.redirectUri) = (nonce, challenge, redirectUri);

        public Task<MicrosoftIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, string nonce, CancellationToken ct)
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
