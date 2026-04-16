using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Models;

public class ShiftIdentityCookieAuthOptions
{
    /// <summary>
    /// Name of the authentication cookie.
    /// </summary>
    public string CookieName { get; set; } = ".ShiftIdentity.Auth";

    /// <summary>
    /// Whether identity is hosted internally (same process) or externally (separate server).
    /// </summary>
    public ShiftIdentityHostingTypes HostingType { get; set; }

    /// <summary>
    /// Base URL of the external identity server API.
    /// Required when HostingType is External.
    /// </summary>
    public string? ExternalIdentityApiUrl { get; set; }

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

    /// <summary>
    /// Path to redirect to when unauthenticated.
    /// </summary>
    public string LoginPath { get; set; } = "/Identity/login";
}
