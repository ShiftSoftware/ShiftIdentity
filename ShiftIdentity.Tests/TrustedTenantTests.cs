using System.Security.Claims;
using Microsoft.AspNetCore.WebUtilities;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The trusted Microsoft tenants as configured: what fails at startup, which emails are held to a tenant, what the login
/// screen is told, and the authorize URL an administrator confirmation's popup opens.
/// </summary>
public sealed class TrustedTenantTests
{
    private static readonly string Tenant = Guid.NewGuid().ToString("D");

    private static MicrosoftSignInSettings Settings(params TrustedTenantSettings[] tenants) =>
        new() { Enabled = true, ClientId = Guid.NewGuid().ToString(), ClientSecret = "secret", TrustedTenants = [.. tenants] };

    private static TrustedTenants Create(MicrosoftSignInSettings settings) =>
        TrustedTenants.Create(settings, (name, requirement) => new InvalidOperationException(name + " " + requirement));

    [Fact]
    public void No_trusted_tenants_hold_no_email()
    {
        var none = Create(Settings());
        Assert.False(none.Any);
        Assert.Null(none.ForEmail("person@example.com"));
    }

    [Theory]
    [InlineData("person@contoso.example", true)]
    [InlineData("  Person@CONTOSO.Example ", true)]
    [InlineData("person@mail.contoso.example", false)]
    [InlineData("person@contoso.example.evil", false)]
    [InlineData("contoso.example", false)]
    [InlineData(null, false)]
    public void An_email_is_held_to_a_tenant_by_its_exact_domain_in_any_case(string? email, bool held)
    {
        var tenants = Create(Settings(new TrustedTenantSettings { TenantId = Tenant.ToUpperInvariant(), Domains = ["Contoso.Example", "@fabrikam.example"] }));
        Assert.Equal(held, tenants.ForEmail(email) is { } tenant && tenant.ID == Tenant);
        Assert.Equal(["contoso.example", "fabrikam.example"], tenants.Domains);
        Assert.True(tenants.Find(Tenant)!.SkipShiftMfa);
    }

    [Theory]
    [InlineData("not-a-guid", "contoso.example", "TenantId")]
    [InlineData("9188040d-6c67-4c5b-b112-36a304b66dad", "contoso.example", "personal Microsoft account")]
    [InlineData(null, "contoso.example", "TenantId")]
    [InlineData("tenant", "contoso", "domain")]
    [InlineData("tenant", "", "domain")]
    [InlineData("tenant", "bad domain.example", "domain")]
    public void A_malformed_tenant_or_domain_fails_at_startup(string? tenant, string domain, string message)
    {
        var settings = Settings(new TrustedTenantSettings { TenantId = tenant == "tenant" ? Tenant : tenant, Domains = [domain] });
        Assert.Contains(message, Assert.Throws<InvalidOperationException>(() => Create(settings)).Message);
    }

    [Fact]
    public void A_tenant_without_domains_a_repeated_domain_or_tenant_and_Microsoft_off_fail_at_startup()
    {
        Assert.Contains("at least one", Assert.Throws<InvalidOperationException>(() => Create(Settings(new TrustedTenantSettings { TenantId = Tenant }))).Message);
        Assert.Contains("more than once", Assert.Throws<InvalidOperationException>(() => Create(Settings(
            new TrustedTenantSettings { TenantId = Tenant, Domains = ["contoso.example"] },
            new TrustedTenantSettings { TenantId = Guid.NewGuid().ToString(), Domains = ["CONTOSO.example"] }))).Message);
        Assert.Contains("twice", Assert.Throws<InvalidOperationException>(() => Create(Settings(
            new TrustedTenantSettings { TenantId = Tenant, Domains = ["contoso.example"] },
            new TrustedTenantSettings { TenantId = Tenant, Domains = ["fabrikam.example"] }))).Message);
        var off = Settings(new TrustedTenantSettings { TenantId = Tenant, Domains = ["contoso.example"] });
        off.Enabled = false;
        Assert.Contains("needs Microsoft sign-in", Assert.Throws<InvalidOperationException>(() => Create(off)).Message);
    }

    [Fact]
    public void The_registration_validates_them_with_the_rest_of_the_authority()
    {
        Assert.Equal(TrustedTenants.None, Create(new MicrosoftSignInSettings { Enabled = false }));
    }

    [Theory]
    [InlineData("person@contoso.example", true)]
    [InlineData("PERSON@Contoso.Example", true)]
    [InlineData("person@other.example", false)]
    [InlineData("contoso.example", false)]
    [InlineData("username", false)]
    public void The_login_screen_recognises_an_email_on_a_trusted_domain(string typed, bool microsoft)
    {
        Assert.Equal(microsoft, new SignInProviders(true, false, ["contoso.example"]).RequiresMicrosoft(typed));
        Assert.False(new SignInProviders(true).RequiresMicrosoft(typed));
    }

    [Fact]
    public void The_confirmation_popup_forces_a_new_Microsoft_sign_in_for_the_account_and_its_domain()
    {
        var signIn = new MicrosoftSignIn(Settings(), "https://identity.example", new NoTokens());
        var url = new Uri(signIn.ConfirmationUrl("state", "nonce", "challenge", "https://api.example/callback", "person@contoso.example", "contoso.example"));
        var query = QueryHelpers.ParseQuery(url.Query);
        Assert.Equal(("login", "person@contoso.example", "contoso.example", "S256"),
            (query["prompt"].ToString(), query["login_hint"].ToString(), query["domain_hint"].ToString(), query["code_challenge_method"].ToString()));
        Assert.Equal("https://identity.example/_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/provider-confirmation.html#result=ok&provider=microsoft",
            signIn.ConfirmationReturnUrl(null));
        Assert.EndsWith("#error=ProviderNotAllowedForAccount&provider=microsoft", signIn.ConfirmationReturnUrl(AuthenticationFailure.ProviderNotAllowedForAccount));
    }

    [Fact]
    public void A_Microsoft_token_reports_when_the_person_authenticated()
    {
        var at = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds();
        var identity = MicrosoftSignIn.Read(new ClaimsIdentity([new("tid", Tenant), new("oid", Guid.NewGuid().ToString()),
            new("email", "person@contoso.example"), new("xms_edov", "true"), new("auth_time", at.ToString())]));
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(at), identity!.AuthenticatedAt);
        Assert.Null(MicrosoftSignIn.Read(new ClaimsIdentity([new("tid", Tenant), new("oid", Guid.NewGuid().ToString())]))!.AuthenticatedAt);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("https://sts.windows.net/{tenant}/", false)]
    [InlineData("https://sts.windows.net/9188040d-6c67-4c5b-b112-36a304b66dad/", true)]
    [InlineData("live.com", true)]
    [InlineData("https://sts.windows.net/11111111-2222-3333-4444-555555555555/", true)]
    public void A_Microsoft_account_whose_idp_is_another_provider_is_a_guest_and_carries_no_tenant(string? idp, bool guest)
    {
        Claim[] claims = [new("tid", Tenant), new("oid", Guid.NewGuid().ToString()), new("email", "person@contoso.example"), new("xms_edov", "true")];
        if (idp is not null) claims = [.. claims, new("idp", idp.Replace("{tenant}", Tenant))];
        var identity = MicrosoftSignIn.Read(new ClaimsIdentity(claims))!;
        Assert.Equal(guest, identity.Guest);
        Assert.Equal(guest ? null : Tenant, identity.SessionTenant(SignInProvider.Microsoft));
        Assert.Null(identity.SessionTenant(SignInProvider.Google));
    }

    [Fact]
    public void The_Google_confirmation_asks_for_a_new_sign_in_and_its_time()
    {
        var google = new GoogleSignIn(new GoogleSignInSettings { Enabled = true, ClientId = "test.apps.googleusercontent.com", ClientSecret = "secret" },
            "https://identity.example", new NoTokens());
        var query = QueryHelpers.ParseQuery(new Uri(google.ConfirmationUrl("state", "nonce", "challenge", "https://api.example/callback", "person@gmail.com", null)).Query);
        Assert.Equal(("0", "person@gmail.com", """{"id_token":{"auth_time":{"essential":true}}}"""),
            (query["max_age"].ToString(), query["login_hint"].ToString(), query["claims"].ToString()));
    }

    private sealed class NoTokens : IProviderTokenClient
    {
        public Task<ProviderIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, string nonce, CancellationToken ct) =>
            Task.FromResult<ProviderIdentity?>(null);
    }
}
