using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

/// <summary>
/// What a validated provider ID token says about the person: the provider's directory and its stable identifier of
/// the person there, the email and whether the provider vouches for it, and whether the account is a personal one.
/// It authorizes nothing by itself.
/// </summary>
internal sealed record ProviderIdentity(string Directory, string Subject, string? Email, bool EmailVerified, bool PersonalAccount);

/// <summary>The protocol boundary: redeems an authorization code and returns the validated identity, or null.</summary>
internal interface IProviderTokenClient
{
    Task<ProviderIdentity?> RedeemAsync(string code, string codeVerifier, string redirectUri, string nonce, CancellationToken cancellationToken);
}

/// <summary>
/// One sign-in provider for one host: its OAuth client, where the browser goes and where it comes back. Which Shift
/// account a provider identity reaches is decided by admission, never here.
/// </summary>
internal abstract class ProviderSignIn(SignInProvider provider, string clientId, string? redirectUri, bool requireShiftMfa,
    string? frontEndUrl, IProviderTokenClient tokens)
{
    public SignInProvider Provider { get; } = provider;
    /// <summary>The provider's name in its routes, its return fragment and the session's route claim.</summary>
    public string Name { get; } = RouteName(provider);
    public string ClientId { get; } = clientId.Trim();
    public bool RequireShiftMfa { get; } = requireShiftMfa;
    public IProviderTokenClient Tokens { get; } = tokens;

    /// <summary>The configured callback, or this API's as the request reached it: HTTPS except on a loopback host.</summary>
    public string CallbackUri(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(redirectUri)) return redirectUri.Trim();
        var host = request.Host.Host;
        var loopback = host is "localhost" or "127.0.0.1" or "[::1]";
        var scheme = loopback ? request.Scheme : "https";
        var port = request.Host.Port is { } value && !(scheme == "https" && value == 443) && !(scheme == "http" && value == 80) ? ":" + value : "";
        return $"{scheme}://{host}{port}{request.PathBase}/api/identity/v2/providers/{Name}/callback";
    }

    public abstract string AuthorizeUrl(string state, string nonce, string codeChallenge, string redirectUri);

    /// <summary>The login screen the browser returns to: a handle to complete with, or why there is none. Both travel in the fragment.</summary>
    public string ReturnUrl(string? handle, AuthenticationFailure? failure)
    {
        var login = (string.IsNullOrWhiteSpace(frontEndUrl) ? "" : frontEndUrl.Trim().TrimEnd('/')) + "/" + Core.Constants.IdentityRoutePreifix + "/login";
        return handle is not null
            ? login + "#" + Name + "=" + Uri.EscapeDataString(handle)
            : login + "#" + Name + "-error=" + (failure ?? AuthenticationFailure.InvalidGrant);
    }

    internal static string RouteName(SignInProvider provider) => provider switch
    {
        SignInProvider.Microsoft => "microsoft",
        SignInProvider.Google => "google",
        _ => throw new InvalidOperationException("Unknown sign-in provider.")
    };

    internal static SignInProvider? FromRouteName(string? name) => name switch
    {
        "microsoft" => SignInProvider.Microsoft,
        "google" => SignInProvider.Google,
        _ => null
    };

    /// <summary>The single value of a claim, or null when it is missing or repeated.</summary>
    internal static string? One(ClaimsIdentity claims, string type) =>
        claims.FindAll(type).Select(x => x.Value).ToArray() is [var value] ? value : null;

    /// <summary>The token's email when it is a plain, well-formed address; null otherwise.</summary>
    internal static string? ReadEmail(ClaimsIdentity claims)
    {
        var email = One(claims, "email")?.Trim();
        return email is null || email.Length is 0 or > 255 || !MailAddress.TryCreate(email, out var address) || address.Address != email
            ? null : email;
    }

    internal static bool IsTrue(string? value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

    /// <summary>The token carries exactly one nonce, and it is the one this sign-in sent.</summary>
    internal static bool NonceMatches(ClaimsIdentity claims, string nonce) =>
        claims.FindAll("nonce").ToArray() is [var claim] && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(claim.Value), System.Text.Encoding.UTF8.GetBytes(nonce));
}
