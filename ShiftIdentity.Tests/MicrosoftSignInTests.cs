using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The Microsoft protocol boundary without Microsoft: ID token validation against a known key, the rule that only an
/// email Microsoft verified can match an account, and the URLs the flow sends the browser to.
/// </summary>
public sealed class MicrosoftSignInTests
{
    private static readonly string ClientId = Guid.NewGuid().ToString();
    private static readonly string Tenant = Guid.NewGuid().ToString();
    private static readonly string Person = Guid.NewGuid().ToString();
    private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly MicrosoftSignInSettings settings = new() { Enabled = true, ClientId = ClientId, ClientSecret = "secret" };

    private MicrosoftTokenClient Client()
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(key);
        return new(settings, new HttpClient(), new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    private string Token(Dictionary<string, object> claims, string? issuer = null, string? audience = null, DateTime? expires = null, SecurityKey? signedWith = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? $"https://login.microsoftonline.com/{claims["tid"]}/v2.0", Audience = audience ?? ClientId,
            Claims = claims, IssuedAt = DateTime.UtcNow.AddMinutes(-1), NotBefore = DateTime.UtcNow.AddMinutes(-1),
            Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(signedWith ?? key, SecurityAlgorithms.RsaSha256)
        });

    private static Dictionary<string, object> Claims(string tenant = "", string email = "person@example.invalid", object? edov = null, string nonce = "nonce")
    {
        var claims = new Dictionary<string, object> { ["tid"] = tenant.Length == 0 ? Tenant : tenant, ["oid"] = Person, ["email"] = email, ["nonce"] = nonce };
        if (edov is not null) claims["xms_edov"] = edov;
        return claims;
    }

    [Fact]
    public async Task A_valid_work_account_token_with_a_domain_verified_email_is_read()
    {
        var identity = await Client().ValidateAsync(Token(Claims(edov: true)), "nonce", default);
        Assert.Equal(new MicrosoftIdentity(Tenant, Person, "person@example.invalid", true), identity);
    }

    [Fact]
    public async Task A_personal_account_email_counts_as_verified_and_a_work_email_without_xms_edov_does_not()
    {
        Assert.True((await Client().ValidateAsync(Token(Claims(MicrosoftSignIn.ConsumerTenant)), "nonce", default))!.EmailVerified);
        Assert.False((await Client().ValidateAsync(Token(Claims()), "nonce", default))!.EmailVerified);
        Assert.False((await Client().ValidateAsync(Token(Claims(edov: false)), "nonce", default))!.EmailVerified);
        Assert.False((await Client().ValidateAsync(Token(Claims(email: "not an email", edov: true)), "nonce", default))!.EmailVerified);
    }

    [Fact]
    public async Task Wrong_audience_issuer_tenant_nonce_signature_or_expiry_is_refused()
    {
        var client = Client();
        Assert.Null(await client.ValidateAsync(Token(Claims(edov: true), audience: Guid.NewGuid().ToString()), "nonce", default));
        // The common endpoint signs for every tenant: an issuer naming another tenant than the token's is refused.
        Assert.Null(await client.ValidateAsync(Token(Claims(edov: true), issuer: $"https://login.microsoftonline.com/{Guid.NewGuid()}/v2.0"), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(edov: true)), "another nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(edov: true), signedWith: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(edov: true), expires: DateTime.UtcNow.AddMinutes(-5)), "nonce", default));
    }

    [Fact]
    public void Identity_without_tenant_or_object_is_unreadable()
    {
        Assert.Null(MicrosoftSignIn.Read(new ClaimsIdentity([new Claim("oid", Person)])));
        Assert.Null(MicrosoftSignIn.Read(new ClaimsIdentity([new Claim("tid", "not-a-guid"), new Claim("oid", Person)])));
        Assert.Null(MicrosoftSignIn.Read(new ClaimsIdentity([new Claim("tid", Tenant), new Claim("oid", Person), new Claim("oid", Person)])));
    }

    [Fact]
    public void Authorize_url_uses_the_code_flow_with_PKCE_and_account_selection()
    {
        var url = new MicrosoftSignIn(settings, null, Client()).AuthorizeUrl("state", "nonce", "challenge", "https://api.example.invalid/cb");
        Assert.StartsWith("https://login.microsoftonline.com/common/oauth2/v2.0/authorize?", url);
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        Assert.Equal((ClientId, "code", "openid profile email", "S256", "challenge", "select_account", "state", "nonce", "https://api.example.invalid/cb"),
            (query["client_id"].ToString(), query["response_type"].ToString(), query["scope"].ToString(), query["code_challenge_method"].ToString(),
                query["code_challenge"].ToString(), query["prompt"].ToString(), query["state"].ToString(), query["nonce"].ToString(), query["redirect_uri"].ToString()));
    }

    [Theory]
    [InlineData("http", "tca-identity.azurewebsites.net", null, "https://tca-identity.azurewebsites.net/api/identity/v2/providers/microsoft/callback")]
    [InlineData("http", "localhost", 5223, "http://localhost:5223/api/identity/v2/providers/microsoft/callback")]
    [InlineData("https", "identity.example.invalid", 443, "https://identity.example.invalid/api/identity/v2/providers/microsoft/callback")]
    public void Callback_is_https_except_on_loopback(string scheme, string host, int? port, string expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = scheme;
        context.Request.Host = port is null ? new HostString(host) : new HostString(host, port.Value);
        Assert.Equal(expected, new MicrosoftSignIn(settings, null, Client()).CallbackUri(context.Request));
        var configured = new MicrosoftSignInSettings { Enabled = true, ClientId = ClientId, ClientSecret = "s", RedirectUri = "https://proxy.example.invalid/cb" };
        Assert.Equal("https://proxy.example.invalid/cb", new MicrosoftSignIn(configured, null, Client()).CallbackUri(context.Request));
    }

    [Fact]
    public void Return_url_carries_the_handle_or_the_failure_in_the_fragment()
    {
        var signIn = new MicrosoftSignIn(settings, "https://identity.example.invalid/", Client());
        Assert.Equal("https://identity.example.invalid/Identity/login#microsoft=a.b", signIn.ReturnUrl("a.b", null));
        Assert.Equal("https://identity.example.invalid/Identity/login#microsoft-error=ProviderAccountNotFound",
            signIn.ReturnUrl(null, AuthenticationFailure.ProviderAccountNotFound));
    }

    [Theory]
    [InlineData("not-a-guid", "secret", null)]
    [InlineData(null, "secret", null)]
    [InlineData("8f1b3c1e-0000-4000-8000-000000000000", "secret", "https://api.example.invalid/cb?x=1")]
    [InlineData("8f1b3c1e-0000-4000-8000-000000000000", "secret", "relative/cb")]
    public void Enabled_Microsoft_sign_in_without_a_usable_registration_fails_at_startup(string? clientId, string secret, string? redirect)
    {
        var error = Assert.Throws<InvalidOperationException>(() => IdentityAuthorityRegistration.ValidateMicrosoft(
            new MicrosoftSignInSettings { Enabled = true, ClientId = clientId, ClientSecret = secret, RedirectUri = redirect }));
        Assert.Contains("Authority.Microsoft.", error.Message);
        Assert.Null(IdentityAuthorityRegistration.ValidateMicrosoft(new MicrosoftSignInSettings { Enabled = false }));
        Assert.Null(IdentityAuthorityRegistration.ValidateMicrosoft(new MicrosoftSignInSettings
            { Enabled = true, ClientId = "8f1b3c1e-0000-4000-8000-000000000000", ClientSecret = "secret" }));
        // An empty secret starts the host with Microsoft sign-in off and a warning, so settings files can hold the empty key.
        Assert.Contains("ClientSecret", IdentityAuthorityRegistration.ValidateMicrosoft(new MicrosoftSignInSettings
            { Enabled = true, ClientId = "8f1b3c1e-0000-4000-8000-000000000000", ClientSecret = "" }));
    }

    [Fact]
    public void The_link_notice_names_the_Microsoft_account_and_opens_the_login_screen()
    {
        var content = SecurityEmailTemplate.Render(new SecurityEmail(Guid.NewGuid(), "owner@example.invalid", "subject", "",
            AuthenticationOperationPurpose.ProviderLogin, DateTimeOffset.UtcNow)
        { Username = "owner", FullName = "Owner <b>", ProviderAccount = "owner@outlook.example.invalid" }, "https://identity.example.invalid", null);
        Assert.Equal("Microsoft sign-in linked to your account", content.Subject);
        Assert.Contains("owner@outlook.example.invalid", content.TextBody);
        Assert.Contains("https://identity.example.invalid/Identity/login", content.HtmlBody);
        Assert.Contains("Owner &lt;b&gt;", content.HtmlBody);
        Assert.DoesNotContain("#grant=", content.HtmlBody);
    }
}
