namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

public class ShiftIdentityCookieAuthOptions
{
    /// <summary>
    /// Name of the authentication cookie.
    /// </summary>
    public string CookieName { get; set; } = ".ShiftIdentity.Auth";

    /// <summary>
    /// How long the auth cookie remains valid.
    /// </summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Issuer of JWTs minted by the external identity server. Required for external hosting:
    /// every token returned by /Auth/Login, /Auth/Refresh, and /Auth/CompletePasswordChange
    /// is validated against this issuer before its claims are bound to the local cookie.
    /// </summary>
    public string? JwtIssuer { get; set; }

    /// <summary>
    /// Base64-encoded RSA public key (PKCS#1) used to verify external JWT signatures. Must
    /// match the private key the external identity server signs with. Required for external
    /// hosting. Same format as <c>tokenRSAPublicKeyBase64</c> passed to <c>AddShiftIdentityApi</c>.
    /// </summary>
    public string? JwtPublicKey { get; set; }
}
