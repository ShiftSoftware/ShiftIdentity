using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// Sign in with Google for one host: its OAuth client and where the browser goes. Any Google account may sign in;
/// which Shift account it reaches is decided by admission.
/// </summary>
internal sealed class GoogleSignIn(GoogleSignInSettings settings, string? frontEndUrl, IProviderTokenClient tokens)
    : ProviderSignIn(SignInProvider.Google, settings.ClientId!, settings.RedirectUri, settings.RequireShiftMfa, frontEndUrl, tokens)
{
    /// <summary>Google's issuer, and the directory every Google link records. Tokens may also carry it without the scheme.</summary>
    internal const string Issuer = "https://accounts.google.com";
    internal const string ShortIssuer = "accounts.google.com";

    public override string AuthorizeUrl(string state, string nonce, string codeChallenge, string redirectUri) =>
        "https://accounts.google.com/o/oauth2/v2/auth" + QueryString.Create(new Dictionary<string, string?>
        {
            // Only the email is used, so only the email is asked for. No offline access: Shift never calls Google again.
            ["client_id"] = ClientId, ["response_type"] = "code", ["redirect_uri"] = redirectUri,
            ["scope"] = "openid email", ["state"] = state, ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge, ["code_challenge_method"] = "S256",
            // A shared computer must not continue silently as whoever last signed in to Google there.
            ["prompt"] = "select_account"
        }).ToUriComponent();

    /// <summary>
    /// The identity in a validated token, bound to Google's issuer and <c>sub</c> (stable, never reused, the same for
    /// every OAuth client). The email counts as verified when Google says so (<c>email_verified</c>): a Gmail or
    /// Workspace address, or another address Google checked when the account was made (owner decision, 7 October
    /// 2026). An account in a Workspace or Cloud Identity organization (<c>hd</c>) is a work or school account.
    /// </summary>
    internal static ProviderIdentity? Read(ClaimsIdentity claims)
    {
        if (One(claims, "iss") is not (Issuer or ShortIssuer)) return null;
        var subject = One(claims, "sub");
        if (subject is not { Length: > 0 and <= 255 } || subject.Any(x => x is < '!' or > '~')) return null;
        var email = ReadEmail(claims);
        var verified = email is not null && IsTrue(One(claims, "email_verified"));
        var workspace = !string.IsNullOrWhiteSpace(One(claims, "hd"));
        return new(Issuer, subject, email, verified, !workspace);
    }
}

/// <summary>Redeems the code at Google's token endpoint and validates the ID token against Google's published keys.</summary>
internal sealed class GoogleTokenClient(GoogleSignInSettings settings, HttpClient http,
    IConfigurationManager<OpenIdConnectConfiguration>? metadata = null) : IProviderTokenClient
{
    private readonly IConfigurationManager<OpenIdConnectConfiguration> metadata = metadata ??
        new ConfigurationManager<OpenIdConnectConfiguration>(GoogleSignIn.Issuer + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(http) { RequireHttps = true });

    public async Task<ProviderIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, string nonce, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId!.Trim(), ["client_secret"] = settings.ClientSecret!, ["grant_type"] = "authorization_code",
                ["code"] = code, ["redirect_uri"] = redirectUri, ["code_verifier"] = codeVerifier
            }), ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (body?.IdToken is not { Length: > 0 } idToken) return null;
        return await ValidateAsync(idToken, nonce, ct);
    }

    internal async Task<ProviderIdentity?> ValidateAsync(string idToken, string nonce, CancellationToken ct)
    {
        var configuration = await metadata.GetConfigurationAsync(ct);
        var clientId = settings.ClientId!.Trim();
        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidAudience = clientId, ValidateAudience = true,
            ValidIssuers = [GoogleSignIn.Issuer, GoogleSignIn.ShortIssuer], ValidateIssuer = true,
            IssuerSigningKeys = configuration.SigningKeys, ValidateIssuerSigningKey = true, RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateLifetime = true, RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        });
        if (!result.IsValid || !ProviderSignIn.NonceMatches(result.ClaimsIdentity, nonce)) return null;
        // The party the token was issued to, when named, is this client.
        var parties = result.ClaimsIdentity.FindAll("azp").ToArray();
        if (parties.Length > 1 || parties is [var party] && party.Value != clientId) return null;
        return GoogleSignIn.Read(result.ClaimsIdentity);
    }

    private sealed record TokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}
