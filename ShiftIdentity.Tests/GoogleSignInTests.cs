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
/// The Google protocol boundary without Google: ID token validation against a known key, the rule that only an email
/// Google marks verified can match an account, the identity it binds to, and the URLs the flow sends the browser to.
/// </summary>
public sealed class GoogleSignInTests
{
    private const string ClientId = "1234-test.apps.googleusercontent.com";
    private const string Subject = "109876543210987654321";
    private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "test-key" };
    private readonly GoogleSignInSettings settings = new() { Enabled = true, ClientId = ClientId, ClientSecret = "secret" };

    private GoogleTokenClient Client()
    {
        var configuration = new OpenIdConnectConfiguration();
        configuration.SigningKeys.Add(key);
        return new(settings, new HttpClient(), new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
    }

    private string Token(Dictionary<string, object> claims, string issuer = GoogleSignIn.Issuer, string audience = ClientId,
        DateTime? expires = null, SecurityKey? signedWith = null, string algorithm = SecurityAlgorithms.RsaSha256) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer, Audience = audience, Claims = claims,
            IssuedAt = DateTime.UtcNow.AddMinutes(-1), NotBefore = DateTime.UtcNow.AddMinutes(-1), Expires = expires ?? DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = new SigningCredentials(signedWith ?? key, algorithm)
        });

    private static Dictionary<string, object> Claims(string email = "person@gmail.com", object? verified = null, string? hd = null,
        string nonce = "nonce", string? azp = ClientId, string subject = Subject)
    {
        var claims = new Dictionary<string, object> { ["sub"] = subject, ["email"] = email, ["email_verified"] = verified ?? true, ["nonce"] = nonce };
        if (hd is not null) claims["hd"] = hd;
        if (azp is not null) claims["azp"] = azp;
        return claims;
    }

    [Fact]
    public async Task A_valid_Gmail_token_is_read_as_a_verified_personal_account_bound_to_issuer_and_subject()
    {
        var identity = await Client().ValidateAsync(Token(Claims()), "nonce", default);
        Assert.Equal(new ProviderIdentity(GoogleSignIn.Issuer, Subject, "person@gmail.com", true, true), identity);
    }

    [Fact]
    public async Task Both_Google_issuer_forms_are_accepted_and_recorded_as_one_directory()
    {
        var identity = await Client().ValidateAsync(Token(Claims(), issuer: GoogleSignIn.ShortIssuer), "nonce", default);
        Assert.Equal(GoogleSignIn.Issuer, identity!.Directory);
    }

    [Fact]
    public async Task A_Workspace_account_is_a_work_account()
    {
        var identity = await Client().ValidateAsync(Token(Claims("aza@shift.example.invalid", hd: "shift.example.invalid")), "nonce", default);
        Assert.Equal((true, false), (identity!.EmailVerified, identity.PersonalAccount));
    }

    [Fact]
    public async Task Any_email_Google_marks_verified_counts_and_an_unverified_or_malformed_one_does_not()
    {
        // A Google account on an address Google does not host (owner decision, 7 October 2026).
        Assert.True((await Client().ValidateAsync(Token(Claims("a.asim@toyota.example.invalid")), "nonce", default))!.EmailVerified);
        Assert.True((await Client().ValidateAsync(Token(Claims(verified: "true")), "nonce", default))!.EmailVerified);
        Assert.False((await Client().ValidateAsync(Token(Claims(verified: false)), "nonce", default))!.EmailVerified);
        Assert.False((await Client().ValidateAsync(Token(Claims(verified: "false")), "nonce", default))!.EmailVerified);
        var malformed = await Client().ValidateAsync(Token(Claims("not an email")), "nonce", default);
        Assert.Equal((null, false), (malformed!.Email, malformed.EmailVerified));
        var noFlag = Claims();
        noFlag.Remove("email_verified");
        Assert.False((await Client().ValidateAsync(Token(noFlag), "nonce", default))!.EmailVerified);
    }

    [Fact]
    public async Task Wrong_audience_issuer_nonce_party_signature_algorithm_or_expiry_is_refused()
    {
        var client = Client();
        Assert.Null(await client.ValidateAsync(Token(Claims(), audience: "other.apps.googleusercontent.com"), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(), issuer: "https://accounts.example.invalid"), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims()), "another nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(azp: "other.apps.googleusercontent.com")), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(), signedWith: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" }), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(), algorithm: SecurityAlgorithms.RsaSha512), "nonce", default));
        Assert.Null(await client.ValidateAsync(Token(Claims(), expires: DateTime.UtcNow.AddMinutes(-5)), "nonce", default));
        // A token without azp is still bound to this client by its audience.
        Assert.NotNull(await client.ValidateAsync(Token(Claims(azp: null)), "nonce", default));
    }

    [Fact]
    public void Identity_without_a_usable_subject_or_from_another_issuer_is_unreadable()
    {
        ClaimsIdentity With(params Claim[] claims) => new(claims);
        Assert.Null(GoogleSignIn.Read(With(new Claim("iss", GoogleSignIn.Issuer))));
        Assert.Null(GoogleSignIn.Read(With(new Claim("iss", GoogleSignIn.Issuer), new Claim("sub", new string('1', 256)))));
        Assert.Null(GoogleSignIn.Read(With(new Claim("iss", GoogleSignIn.Issuer), new Claim("sub", "has space"))));
        Assert.Null(GoogleSignIn.Read(With(new Claim("iss", GoogleSignIn.Issuer), new Claim("sub", Subject), new Claim("sub", Subject))));
        Assert.Null(GoogleSignIn.Read(With(new Claim("iss", "https://login.microsoftonline.com/x/v2.0"), new Claim("sub", Subject))));
        Assert.Equal(255, GoogleSignIn.Read(With(new Claim("iss", GoogleSignIn.Issuer), new Claim("sub", new string('1', 255))))!.Subject.Length);
    }

    [Fact]
    public void Authorize_url_uses_the_code_flow_with_PKCE_account_selection_and_only_the_email_scope()
    {
        var url = new GoogleSignIn(settings, null, Client()).AuthorizeUrl("state", "nonce", "challenge", "https://api.example.invalid/cb");
        Assert.StartsWith("https://accounts.google.com/o/oauth2/v2/auth?", url);
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        Assert.Equal((ClientId, "code", "openid email", "S256", "challenge", "select_account", "state", "nonce", "https://api.example.invalid/cb"),
            (query["client_id"].ToString(), query["response_type"].ToString(), query["scope"].ToString(), query["code_challenge_method"].ToString(),
                query["code_challenge"].ToString(), query["prompt"].ToString(), query["state"].ToString(), query["nonce"].ToString(), query["redirect_uri"].ToString()));
        Assert.False(query.ContainsKey("access_type"));
        Assert.False(query.ContainsKey("hd"));
    }

    [Fact]
    public void Callback_and_return_url_use_the_google_route_and_fragment()
    {
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("tca-identity.azurewebsites.net");
        var signIn = new GoogleSignIn(settings, "https://identity.example.invalid/", Client());
        Assert.Equal("https://tca-identity.azurewebsites.net/api/identity/v2/providers/google/callback", signIn.CallbackUri(context.Request));
        Assert.Equal("https://identity.example.invalid/Identity/login#google=a.b", signIn.ReturnUrl("a.b", null));
        Assert.Equal("https://identity.example.invalid/Identity/login#google-error=ProviderEmailUnverified",
            signIn.ReturnUrl(null, AuthenticationFailure.ProviderEmailUnverified));
        var configured = new GoogleSignInSettings { Enabled = true, ClientId = ClientId, ClientSecret = "s", RedirectUri = "https://proxy.example.invalid/cb" };
        Assert.Equal("https://proxy.example.invalid/cb", new GoogleSignIn(configured, null, Client()).CallbackUri(context.Request));
    }

    [Theory]
    [InlineData("8f1b3c1e-0000-4000-8000-000000000000", null)]
    [InlineData(null, null)]
    [InlineData(".apps.googleusercontent.com", null)]
    [InlineData("has space.apps.googleusercontent.com", null)]
    [InlineData(ClientId, "https://api.example.invalid/cb?x=1")]
    [InlineData(ClientId, "relative/cb")]
    public void Enabled_Google_sign_in_without_a_usable_client_fails_at_startup(string? clientId, string? redirect)
    {
        var error = Assert.Throws<InvalidOperationException>(() => IdentityAuthorityRegistration.ValidateGoogle(
            new GoogleSignInSettings { Enabled = true, ClientId = clientId, ClientSecret = "secret", RedirectUri = redirect }));
        Assert.Contains("Authority.Google.", error.Message);
        Assert.Null(IdentityAuthorityRegistration.ValidateGoogle(new GoogleSignInSettings { Enabled = false }));
        Assert.Null(IdentityAuthorityRegistration.ValidateGoogle(new GoogleSignInSettings { Enabled = true, ClientId = ClientId, ClientSecret = "secret" }));
        // An empty secret starts the host with Google sign-in off and a warning, so settings files can hold the empty key.
        Assert.Contains("ClientSecret", IdentityAuthorityRegistration.ValidateGoogle(new GoogleSignInSettings { Enabled = true, ClientId = ClientId, ClientSecret = "" }));
    }

    [Fact]
    public void The_link_notice_names_the_Google_account()
    {
        var content = SecurityEmailTemplate.Render(new SecurityEmail(Guid.NewGuid(), "owner@example.invalid", "subject", "",
            AuthenticationOperationPurpose.ProviderLogin, DateTimeOffset.UtcNow)
        { Username = "owner", FullName = "Owner", ProviderAccount = "owner@gmail.example.invalid", Provider = SignInProvider.Google },
            "https://identity.example.invalid", null);
        Assert.Equal("Google sign-in linked to your account", content.Subject);
        Assert.Contains("The Google account owner@gmail.example.invalid just signed in", content.TextBody);
        Assert.DoesNotContain("Microsoft", content.HtmlBody);
    }
}
