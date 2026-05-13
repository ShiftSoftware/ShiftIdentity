using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

public class ShiftIdentityCookieAuthOptions
{
    /// <summary>
    /// Name of the authentication cookie.
    /// </summary>
    public string CookieName { get; set; } = ".ShiftIdentity.Auth";

    /// <summary>
    /// JWT issuer for validating tokens in the sign-in-with-token endpoint.
    /// </summary>
    public string JwtIssuer { get; set; } = default!;

    /// <summary>
    /// Base64-encoded RSA public key for validating JWT signatures.
    /// </summary>
    public string JwtPublicKeyBase64 { get; set; } = default!;

    /// <summary>
    /// How long the auth cookie remains valid.
    /// </summary>
    public TimeSpan ExpireTimeSpan { get; set; } = TimeSpan.FromDays(14);
}
