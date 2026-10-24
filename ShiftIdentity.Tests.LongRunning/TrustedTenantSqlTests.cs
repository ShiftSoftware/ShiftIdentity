using System.IdentityModel.Tokens.Jwt;
using System.Net;
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
/// Trusted Microsoft tenants through the real routes and SQL store: an account whose email is on a tenant's domain signs
/// in only with Microsoft through that tenant, never with a password, a reset, a device or another provider, and that
/// tenant's sign-ins skip Shift MFA. An administrator held to a tenant confirms with a Microsoft popup.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class TrustedTenantSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string Domain = "contoso.example.invalid";
    private const string Email = "person@" + Domain;
    private const string UsersWrite = "{\"ShiftIdentityActions\":{\"Users\":[\"w\",\"d\"]}}";
    private static readonly string Tenant = Guid.NewGuid().ToString("D");
    private static readonly string OtherTenant = Guid.NewGuid().ToString("D");
    private readonly ProviderSignInSqlTests.FakeProvider tokens = new();

    public async ValueTask InitializeAsync()
    {
        fixture.Clock = TimeProvider.System;
        await fixture.ResetAsync(mfa: true);
        await SetEmailAsync(Email);
        Trust(skipShiftMfa: true);
        tokens.Identity = Identity(Email);
    }

    public ValueTask DisposeAsync() { fixture.Microsoft = null; fixture.Google = null; fixture.TrustedTenants = TrustedTenants.None; return ValueTask.CompletedTask; }

    private void Trust(bool skipShiftMfa, bool requireShiftMfa = true)
    {
        var settings = new MicrosoftSignInSettings
        {
            Enabled = true, ClientId = Guid.NewGuid().ToString(), ClientSecret = "test", RequireShiftMfa = requireShiftMfa,
            TrustedTenants = [new() { TenantId = Tenant, Domains = [Domain], SkipShiftMfa = skipShiftMfa }]
        };
        fixture.Microsoft = new MicrosoftSignIn(settings, "https://identity.example.invalid", tokens);
        fixture.TrustedTenants = TrustedTenants.Create(settings, (name, requirement) => new InvalidOperationException(name + " " + requirement));
    }

    // Signed in just now, as a confirmation's token must say (auth_time); an ordinary sign-in ignores it.
    private static ProviderIdentity Identity(string email, string? tenant = null) =>
        new(tenant ?? Tenant, Guid.NewGuid().ToString("D"), email, true, tenant == MicrosoftSignIn.ConsumerTenant) { AuthenticatedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task A_guest_of_the_trusted_tenant_is_not_its_user()
    {
        // A personal or other-tenant account invited into the trusted tenant gets that tenant's tid, but idp names its home.
        tokens.Identity = Identity(Email) with { Guest = true };
        using var host = new IdentityHttpHost(fixture);
        Assert.EndsWith("#microsoft-error=ProviderNotAllowedForAccount", await CallbackLocationAsync(host, IdentityHttpHost.Pkce().Challenge));
        // Off the domains it signs in like any Microsoft account: no tenant on the session, so no trusted-tenant MFA skip.
        await SetEmailAsync("guest@elsewhere.example.invalid");
        tokens.Identity = Identity("guest@elsewhere.example.invalid") with { Guest = true };
        var challenge = Assert.IsType<ChallengeRequired>(await SignInAsync(host)).Challenge;
        Assert.Equal(AuthenticationStep.ExistingMfa, challenge.Step);
    }

    [Fact]
    public async Task A_correct_password_is_refused_as_password_sign_in_disabled_and_a_wrong_one_stays_a_wrong_password()
    {
        using var host = new IdentityHttpHost(fixture);
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge)).Code);
        Assert.Equal(AuthenticationFailure.InvalidProof,
            Assert.IsType<AuthenticationRefused>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge, "wrong-password")).Code);
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Set<AuthenticationOperation>().Where(x => x.UserID == fixture.UserID && x.Purpose == AuthenticationOperationPurpose.Login).ToListAsync());
    }

    [Fact]
    public async Task The_trusted_tenant_signs_in_without_Shift_MFA_even_where_the_host_asks_for_it_and_the_session_renews()
    {
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token).Claims.ToList();
        Assert.Contains(claims, x => x.Type == "shift_route" && x.Value == "microsoft");
        Assert.Contains(claims, x => x.Type == "shift_tenant" && x.Value == Tenant);
        Assert.DoesNotContain(claims, x => x.Type == "shift_confirmed");
        Assert.Contains(claims, x => x.Type == "shift_mfa" && x.Value == "false");
        var renewed = Assert.IsType<SessionIssued>(await host.RefreshAsync(session.Session.RefreshToken));
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(renewed.Session.Token).Claims, x => x.Type == "shift_tenant" && x.Value == Tenant);
        Assert.True(Assert.IsType<AuthenticatorStatus>(await host.ReadAuthenticatorAsync(renewed.Session.Token)).MicrosoftOnly);
    }

    [Fact]
    public async Task Without_SkipShiftMfa_the_trusted_tenant_follows_RequireShiftMfa()
    {
        Trust(skipShiftMfa: false);
        using var host = new IdentityHttpHost(fixture);
        var challenge = Assert.IsType<ChallengeRequired>(await SignInAsync(host)).Challenge;
        Assert.Equal(AuthenticationStep.ExistingMfa, challenge.Step);
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData("personal")]
    public async Task Another_tenant_or_a_personal_account_with_the_same_email_is_refused_and_nothing_is_linked(string source)
    {
        tokens.Identity = Identity(Email, source == "personal" ? MicrosoftSignIn.ConsumerTenant : OtherTenant);
        using var host = new IdentityHttpHost(fixture);
        Assert.EndsWith("#microsoft-error=ProviderNotAllowedForAccount", await CallbackLocationAsync(host, IdentityHttpHost.Pkce().Challenge));
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Set<UserProviderLink>().ToListAsync());
        Assert.False((await db.Users.SingleAsync(x => x.ID == fixture.UserID)).EmailVerified);
    }

    [Fact]
    public async Task An_account_off_the_domains_keeps_its_password_and_the_trusted_tenant_still_skips_its_MFA()
    {
        await SetEmailAsync("person@elsewhere.example.invalid");
        tokens.Identity = Identity("person@elsewhere.example.invalid");
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(session.Session.Token).Claims, x => x.Type == "shift_tenant" && x.Value == Tenant);
    }

    [Fact]
    public async Task A_password_session_from_before_the_rule_is_refused_at_its_next_renewal()
    {
        await fixture.ResetAsync();
        await SetEmailAsync("person@elsewhere.example.invalid");
        Trust(skipShiftMfa: true);
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        await SetEmailAsync(Email);
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.RefreshAsync(session.Session.RefreshToken)).Code);
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.ReadAuthenticatorAsync(session.Session.Token)).Code);
    }

    [Fact]
    public async Task Password_change_and_authenticator_changes_are_refused_for_the_account()
    {
        using var host = new IdentityHttpHost(fixture);
        var session = Assert.IsType<SessionIssued>(await SignInAsync(host));
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.StartPasswordChangeAsync(session.Session.Token, IdentityHttpHost.Pkce().Challenge)).Code);
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.StartMfaAsync(session.Session.Token, IdentityHttpHost.Pkce().Challenge, replace: true)).Code);
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.TurnOffMfaAsync(session.Session.Token, "123456")).Code);
    }

    [Fact]
    public async Task A_reset_request_sends_the_sign_in_with_Microsoft_notice_instead_of_a_link()
    {
        await MakeEligibleAsync(fixture.UserID);
        using var host = new IdentityHttpHost(fixture);
        Assert.IsType<SecurityDeliveryRequested>(await Post(host, "password-reset/request", new RequestSecurityEmail(Email)));
        var notice = Assert.Single(((LocalSecurityInbox)fixture.EmailSink!).Messages);
        Assert.Equal((Email, "/Identity/login", "Your account signs in with Microsoft"), (notice.Destination, notice.Link, notice.Subject));
        await using var db = fixture.CreateContext();
        Assert.Empty(await db.Set<AuthenticationOperation>().Where(x => x.Purpose == AuthenticationOperationPurpose.PasswordResetEmail).ToListAsync());
        Assert.Contains("PasswordResetMicrosoftOnly", await db.Set<AuthenticationAuditEvent>().Where(x => x.UserID == fixture.UserID).Select(x => x.Outcome).ToListAsync());
    }

    [Fact]
    public async Task An_administrator_cannot_set_a_password_or_send_a_reset_for_the_account()
    {
        var admin = "tenant-operator-" + Guid.NewGuid().ToString("N");
        await fixture.CreateSyntheticUserAsync(admin, UsersWrite);
        using var host = new IdentityHttpHost(fixture);
        var access = Assert.IsType<SessionIssued>(await Post(host, "login",
            new PasswordLoginRequest(admin, fixture.Password, IdentityHttpHost.Pkce().Challenge))).Session.Token;
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await host.AdminSetPasswordAsync(access, fixture.UserID, SqlIdentityFixture.PolicyValidPassword)).Code);
        Assert.Equal(AuthenticationFailure.PasswordSignInDisabled,
            Assert.IsType<AuthenticationRefused>(await Post(host, "password-reset/admin", new AdminPasswordResetRequest(fixture.UserID, Manual: true), access)).Code);
    }

    [Fact]
    public async Task The_built_in_administrator_is_never_held_to_a_tenant()
    {
        await using (var db = fixture.CreateContext())
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, true));
        try
        {
            using var host = new IdentityHttpHost(fixture);
            Assert.IsType<ChallengeRequired>(await host.LoginAsync(fixture, IdentityHttpHost.Pkce().Challenge));
        }
        finally
        {
            await using var db = fixture.CreateContext();
            await db.Users.Where(x => x.ID == fixture.UserID).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsProtected, false));
        }
    }

    [Fact]
    public async Task An_administrator_held_to_the_tenant_confirms_with_a_fresh_Microsoft_sign_in()
    {
        var (host, access) = await TenantAdministratorAsync();
        using var _ = host;
        var pkce = IdentityHttpHost.Pkce();
        var begun = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", new StartPasswordChangeRequest(pkce.Challenge), access)).Challenge;
        Assert.Equal(AuthenticationStep.ProviderConfirmation, begun.Step);
        var request = new AdministratorProviderConfirmationRequest(begun.Handle!, pkce.Verifier);
        // Before Microsoft returns, completing only says to wait, and the password route never accepts this confirmation.
        Assert.Equal(AuthenticationFailure.AuthorizationPending,
            Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/provider/complete", request, access)).Code);
        var location = await ConfirmationCallbackAsync(host, request, access);
        Assert.Equal("https://identity.example.invalid/_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/provider-confirmation.html#result=ok&provider=microsoft", location);
        var confirmed = Assert.IsType<SessionIssued>(await Post(host, "admin-confirmation/provider/complete", request, access));
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(confirmed.Session.Token).Claims.ToList();
        Assert.Contains(claims, x => x.Type == "shift_tenant" && x.Value == Tenant);
        Assert.Contains(claims, x => x.Type == "shift_confirmed" && x.Value == "true");
        var changed = await host.AdminSetActiveAsync(confirmed.Session.Token, fixture.UserID, false);
        Assert.True(changed is AdminAccountChanged, changed.ToString());
        // Single use.
        Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/provider/complete", request, access));
    }

    [Theory]
    [InlineData("other-tenant")]
    [InlineData("other-email")]
    public async Task A_confirmation_from_another_tenant_or_account_does_not_confirm(string scenario)
    {
        var (host, access) = await TenantAdministratorAsync();
        using var _ = host;
        var pkce = IdentityHttpHost.Pkce();
        var begun = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", new StartPasswordChangeRequest(pkce.Challenge), access)).Challenge;
        var request = new AdministratorProviderConfirmationRequest(begun.Handle!, pkce.Verifier);
        tokens.Identity = scenario == "other-tenant" ? Identity(tokens.Identity!.Email!, OtherTenant) : Identity(Email);
        var location = await ConfirmationCallbackAsync(host, request, access);
        Assert.Contains("#error=" + (scenario == "other-tenant" ? "ProviderNotAllowedForAccount" : "ProviderAccountNotFound"), location);
        Assert.Equal(AuthenticationFailure.AuthorizationPending,
            Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/provider/complete", request, access)).Code);
    }

    // An operator whose email is on the tenant's domain, signed in through Microsoft.
    private async Task<(IdentityHttpHost Host, string Access)> TenantAdministratorAsync()
    {
        var name = "tenant-admin-" + Guid.NewGuid().ToString("N");
        var email = name + "@" + Domain;
        var id = await fixture.CreateSyntheticUserAsync(name, UsersWrite, email: email);
        await SetEmailAsync(email, id);
        tokens.Identity = Identity(email);
        var host = new IdentityHttpHost(fixture);
        return (host, Assert.IsType<SessionIssued>(await SignInAsync(host)).Session.Token);
    }

    [Fact]
    public async Task An_administrator_signed_in_with_Microsoft_off_the_domains_may_confirm_with_Microsoft_beside_the_password()
    {
        var (host, access) = await ProviderAdministratorAsync("microsoft", Identity("operator@elsewhere.example.invalid", OtherTenant));
        using var _ = host;
        var pkce = IdentityHttpHost.Pkce();
        var begun = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", new StartPasswordChangeRequest(pkce.Challenge), access)).Challenge;
        Assert.Equal((AuthenticationStep.Password, (SignInProvider?)SignInProvider.Microsoft), (begun.Step, begun.Provider));
        var request = new AdministratorProviderConfirmationRequest(begun.Handle!, pkce.Verifier);
        Assert.Equal(AuthenticationFailure.AuthorizationPending,
            Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/provider/complete", request, access)).Code);
        var location = await ConfirmationCallbackAsync(host, request, access, "microsoft", query =>
            { Assert.Equal("login", query["prompt"].ToString()); Assert.False(query.ContainsKey("domain_hint")); });
        Assert.EndsWith("#result=ok&provider=microsoft", location);
        // Proven by the provider, the confirmation no longer takes the password.
        Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/password",
            new AdministratorPasswordProofRequest(begun.Handle!, fixture.Password, pkce.Verifier), access));
        var confirmed = Assert.IsType<SessionIssued>(await Post(host, "admin-confirmation/provider/complete", request, access));
        Assert.Contains(new JwtSecurityTokenHandler().ReadJwtToken(confirmed.Session.Token).Claims, x => x.Type == "shift_confirmed");
        var changed = await host.AdminSetActiveAsync(confirmed.Session.Token, fixture.UserID, false);
        Assert.True(changed is AdminAccountChanged, changed.ToString());
    }

    [Fact]
    public async Task A_confirmation_needs_the_token_to_say_the_person_signed_in_just_now()
    {
        fixture.Google = new GoogleSignIn(new GoogleSignInSettings
        { Enabled = true, ClientId = "test.apps.googleusercontent.com", ClientSecret = "test" }, "https://identity.example.invalid", tokens);
        var email = "operator-" + Guid.NewGuid().ToString("N") + "@elsewhere.example.invalid";
        var (host, access) = await ProviderAdministratorAsync("google", new(GoogleSignIn.Issuer, Guid.NewGuid().ToString("N"), email, true, true) { AuthenticatedAt = DateTimeOffset.UtcNow });
        using var _ = host;
        var pkce = IdentityHttpHost.Pkce();
        var begun = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", new StartPasswordChangeRequest(pkce.Challenge), access)).Challenge;
        Assert.Equal(SignInProvider.Google, begun.Provider);
        var request = new AdministratorProviderConfirmationRequest(begun.Handle!, pkce.Verifier);
        // The browser carries the request and can drop max_age, so only auth_time proves a new sign-in: a token without it,
        // or with an old one, does not confirm.
        tokens.Identity = tokens.Identity! with { AuthenticatedAt = null };
        Assert.EndsWith("#error=InvalidGrant&provider=google", await ConfirmationCallbackAsync(host, request, access, "google", query =>
        {
            Assert.Equal("0", query["max_age"].ToString());
            Assert.Contains("auth_time", query["claims"].ToString());
        }));
        tokens.Identity = tokens.Identity with { AuthenticatedAt = DateTimeOffset.UtcNow.AddHours(-1) };
        Assert.EndsWith("#error=Expired&provider=google", await ConfirmationCallbackAsync(host, request, access, "google", _ => { }));
        tokens.Identity = tokens.Identity with { AuthenticatedAt = DateTimeOffset.UtcNow };
        Assert.EndsWith("#result=ok&provider=google", await ConfirmationCallbackAsync(host, request, access, "google", _ => { }));
        Assert.IsType<SessionIssued>(await Post(host, "admin-confirmation/provider/complete", request, access));
    }

    [Theory]
    [InlineData("personal")]
    [InlineData("guest")]
    public async Task A_personal_or_guest_Microsoft_session_confirms_with_the_password_only(string account)
    {
        // Neither can prove a fresh Microsoft sign-in: personal accounts never send auth_time, and a guest's session has no tenant.
        var identity = account == "personal"
            ? Identity("operator-" + Guid.NewGuid().ToString("N") + "@elsewhere.example.invalid", MicrosoftSignIn.ConsumerTenant)
            : Identity("operator-" + Guid.NewGuid().ToString("N") + "@elsewhere.example.invalid", OtherTenant) with { Guest = true };
        var (host, access) = await ProviderAdministratorAsync("microsoft", identity);
        using var _ = host;
        var pkce = IdentityHttpHost.Pkce();
        var begun = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", new StartPasswordChangeRequest(pkce.Challenge), access)).Challenge;
        Assert.Equal((AuthenticationStep.Password, (SignInProvider?)null), (begun.Step, begun.Provider));
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/provider/start",
            new AdministratorProviderConfirmationRequest(begun.Handle!, pkce.Verifier), access)).Code);
        var confirmed = Assert.IsType<SessionIssued>(await Post(host, "admin-confirmation/password",
            new AdministratorPasswordProofRequest(begun.Handle!, fixture.Password, pkce.Verifier), access));
        Assert.True(await host.AdminSetActiveAsync(confirmed.Session.Token, fixture.UserID, false) is AdminAccountChanged);
    }

    [Fact]
    public async Task A_password_session_is_offered_no_provider_and_cannot_start_one()
    {
        var admin = "password-operator-" + Guid.NewGuid().ToString("N");
        await fixture.CreateSyntheticUserAsync(admin, UsersWrite);
        using var host = new IdentityHttpHost(fixture);
        var access = Assert.IsType<SessionIssued>(await Post(host, "login",
            new PasswordLoginRequest(admin, fixture.Password, IdentityHttpHost.Pkce().Challenge))).Session.Token;
        var pkce = IdentityHttpHost.Pkce();
        var begun = Assert.IsType<ChallengeRequired>(await Post(host, "admin-confirmation", new StartPasswordChangeRequest(pkce.Challenge), access)).Challenge;
        Assert.Null(begun.Provider);
        Assert.Equal(AuthenticationFailure.ClientDenied, Assert.IsType<AuthenticationRefused>(await Post(host, "admin-confirmation/provider/start",
            new AdministratorProviderConfirmationRequest(begun.Handle!, pkce.Verifier), access)).Code);
    }

    // An operator off the trusted domains, signed in through the given provider with that identity.
    private async Task<(IdentityHttpHost Host, string Access)> ProviderAdministratorAsync(string provider, ProviderIdentity identity)
    {
        var name = "provider-admin-" + Guid.NewGuid().ToString("N");
        var id = await fixture.CreateSyntheticUserAsync(name, UsersWrite, email: identity.Email);
        await SetEmailAsync(identity.Email!, id);
        tokens.Identity = identity;
        var host = new IdentityHttpHost(fixture);
        return (host, Assert.IsType<SessionIssued>(await SignInAsync(host, provider)).Session.Token);
    }

    private async Task<string> ConfirmationCallbackAsync(IdentityHttpHost host, AdministratorProviderConfirmationRequest request, string access,
        string provider = "microsoft", Action<Dictionary<string, Microsoft.Extensions.Primitives.StringValues>>? check = null)
    {
        var redirect = Assert.IsType<ProviderRedirect>(await Post(host, "admin-confirmation/provider/start", request, access));
        var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
        if (check is null) Assert.Equal(("login", Domain), (query["prompt"].ToString(), query["domain_hint"].ToString()));
        else check(query);
        tokens.Expect(query["nonce"]!, query["code_challenge"]!, query["redirect_uri"]!);
        using var response = await host.Client.GetAsync($"/api/identity/v2/providers/{provider}/callback?code=synthetic-code&state=" + Uri.EscapeDataString(query["state"]!));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }

    private async Task<string> CallbackLocationAsync(IdentityHttpHost host, string challenge, string provider = "microsoft")
    {
        var redirect = Assert.IsType<ProviderRedirect>(await IdentityHttpHost.Read(
            await host.Client.PostAsJsonAsync($"/api/identity/v2/providers/{provider}/start", new StartProviderSignInRequest(challenge))));
        var query = QueryHelpers.ParseQuery(new Uri(redirect.Url).Query);
        tokens.Expect(query["nonce"]!, query["code_challenge"]!, query["redirect_uri"]!);
        using var response = await host.Client.GetAsync($"/api/identity/v2/providers/{provider}/callback?code=synthetic-code&state=" + Uri.EscapeDataString(query["state"]!));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return response.Headers.Location!.ToString();
    }

    private async Task<AuthOutcome> SignInAsync(IdentityHttpHost host, string provider = "microsoft")
    {
        var pkce = IdentityHttpHost.Pkce();
        var location = await CallbackLocationAsync(host, pkce.Challenge, provider);
        var marker = $"#{provider}=";
        Assert.Contains(marker, location);
        var handle = Uri.UnescapeDataString(location[(location.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..]);
        return await IdentityHttpHost.Read(await host.Client.PostAsJsonAsync($"/api/identity/v2/providers/{provider}/complete",
            new CompleteProviderSignInRequest(handle, pkce.Verifier)));
    }

    private async Task SetEmailAsync(string email, long? userID = null)
    {
        var id = userID ?? fixture.UserID;
        await using var db = fixture.CreateContext();
        await db.Users.IgnoreQueryFilters().Where(x => x.ID == id).ExecuteUpdateAsync(x => x
            .SetProperty(u => u.Email, email).SetProperty(u => u.EmailVerified, false));
        await db.Set<UserSecurityState>().Where(x => x.UserID == id).ExecuteUpdateAsync(x => x
            .SetProperty(s => s.EmailLookupKey, RecoveryContact.Key(email)));
    }

    private async Task MakeEligibleAsync(long userID)
    {
        await using var db = fixture.CreateContext();
        var user = await db.Users.SingleAsync(x => x.ID == userID);
        var state = await db.Set<UserSecurityState>().SingleAsync(x => x.UserID == userID);
        RecoveryContact.RecordOwnership(user, state, RecoveryEmailProvenance.TrustedAdminAssignment);
        await db.SaveChangesAsync();
    }

    private static async Task<AuthOutcome> Post<T>(IdentityHttpHost host, string route, T body, string? access = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/identity/v2/" + route) { Content = JsonContent.Create(body) };
        if (access is not null) request.Headers.Authorization = new("Bearer", access);
        return await IdentityHttpHost.Read(await host.Client.SendAsync(request));
    }
}
