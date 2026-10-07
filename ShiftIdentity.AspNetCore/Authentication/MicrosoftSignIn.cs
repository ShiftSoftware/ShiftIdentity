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
/// Sign in with Microsoft for one host: its app registration and where the browser goes. Any work, school or personal
/// Microsoft account may sign in (the "common" endpoint); which Shift account it reaches is decided by admission.
/// </summary>
internal sealed class MicrosoftSignIn(MicrosoftSignInSettings settings, string? frontEndUrl, IProviderTokenClient tokens)
    : ProviderSignIn(SignInProvider.Microsoft, settings.ClientId!, settings.RedirectUri, settings.RequireShiftMfa, frontEndUrl, tokens)
{
    internal const string Authority = "https://login.microsoftonline.com/common/v2.0";
    /// <summary>The tenant that holds every personal Microsoft account. Microsoft verifies those accounts' email addresses.</summary>
    internal const string ConsumerTenant = "9188040d-6c67-4c5b-b112-36a304b66dad";

    public override string AuthorizeUrl(string state, string nonce, string codeChallenge, string redirectUri) =>
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize" + QueryString.Create(new Dictionary<string, string?>
        {
            ["client_id"] = ClientId, ["response_type"] = "code", ["redirect_uri"] = redirectUri,
            ["response_mode"] = "query", ["scope"] = "openid profile email", ["state"] = state, ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge, ["code_challenge_method"] = "S256",
            // A shared computer must not continue silently as whoever last signed in to Microsoft there.
            ["prompt"] = "select_account"
        }).ToUriComponent();

    /// <summary>
    /// The identity in a validated token. An email counts as verified only when Microsoft says so: a personal account's
    /// (Microsoft verified it at sign-up), or a work or school account's with <c>xms_edov</c> true (the domain's owner).
    /// A directory administrator can set any unverified email on a user, so anything else is not proof of the address.
    /// </summary>
    internal static ProviderIdentity? Read(ClaimsIdentity claims)
    {
        var tenant = One(claims, "tid");
        var objectID = One(claims, "oid");
        if (!Guid.TryParse(tenant, out var tenantID) || !Guid.TryParse(objectID, out var objectGuid)) return null;
        var email = ReadEmail(claims);
        var tenantText = tenantID.ToString("D");
        var personal = tenantText == ConsumerTenant;
        var verified = email is not null && (personal || IsTrue(One(claims, "xms_edov")));
        return new(tenantText, objectGuid.ToString("D"), email, verified, personal);
    }
}

/// <summary>Redeems the code at Microsoft's token endpoint and validates the ID token against Microsoft's published keys.</summary>
internal sealed class MicrosoftTokenClient(MicrosoftSignInSettings settings, HttpClient http,
    IConfigurationManager<OpenIdConnectConfiguration>? metadata = null) : IProviderTokenClient
{
    private readonly IConfigurationManager<OpenIdConnectConfiguration> metadata = metadata ??
        new ConfigurationManager<OpenIdConnectConfiguration>(MicrosoftSignIn.Authority + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever(http) { RequireHttps = true });

    public async Task<ProviderIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, string nonce, CancellationToken ct)
    {
        using var response = await http.PostAsync("https://login.microsoftonline.com/common/oauth2/v2.0/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["client_id"] = settings.ClientId!.Trim(), ["client_secret"] = settings.ClientSecret!, ["grant_type"] = "authorization_code",
                ["code"] = code, ["redirect_uri"] = redirectUri, ["code_verifier"] = codeVerifier, ["scope"] = "openid profile email"
            }), ct);
        if (!response.IsSuccessStatusCode) return null;
        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (body?.IdToken is not { Length: > 0 } idToken) return null;
        return await ValidateAsync(idToken, nonce, ct);
    }

    internal async Task<ProviderIdentity?> ValidateAsync(string idToken, string nonce, CancellationToken ct)
    {
        var configuration = await metadata.GetConfigurationAsync(ct);
        var result = await new JsonWebTokenHandler { MapInboundClaims = false }.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidAudience = settings.ClientId!.Trim(), ValidateAudience = true,
            IssuerSigningKeys = configuration.SigningKeys, ValidateIssuerSigningKey = true, RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateLifetime = true, RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            // The common endpoint signs for every tenant: the issuer must name the token's own tenant.
            ValidateIssuer = true,
            IssuerValidator = (issuer, token, parameters) =>
                token is JsonWebToken jwt && jwt.TryGetPayloadValue<string>("tid", out var tid) && Guid.TryParse(tid, out _) &&
                issuer == $"https://login.microsoftonline.com/{tid}/v2.0"
                    ? issuer : throw new SecurityTokenInvalidIssuerException("The issuer does not match the token's tenant.")
        });
        if (!result.IsValid || !ProviderSignIn.NonceMatches(result.ClaimsIdentity, nonce)) return null;
        return MicrosoftSignIn.Read(result.ClaimsIdentity);
    }

    private sealed record TokenResponse([property: JsonPropertyName("id_token")] string? IdToken);
}
