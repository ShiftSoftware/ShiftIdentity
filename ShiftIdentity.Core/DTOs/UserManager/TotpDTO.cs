namespace ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

/// <summary>
/// The data needed to render a TOTP (authenticator app) enrollment QR code.
/// </summary>
public class TotpDTO
{
    /// <summary>The otpauth:// URI that encodes the shared secret.</summary>
    public string? Uri { get; set; }

    /// <summary>An inline SVG document that renders <see cref="Uri"/> as a QR code.</summary>
    public string? Svg { get; set; }

    /// <summary>The Base32-encoded shared secret — shown for manual entry and round-tripped back on confirmation.</summary>
    public string Secret { get; set; } = default!;

    /// <summary>HMAC signature over <see cref="Secret"/> (bound to the user and <see cref="Expires"/>) so it can't be tampered with in transit.</summary>
    public string SasToken { get; set; } = default!;

    /// <summary>Expiry for <see cref="SasToken"/>; round-tripped back on confirmation.</summary>
    public string Expires { get; set; } = default!;

    public string? Code { get; set; }
}
