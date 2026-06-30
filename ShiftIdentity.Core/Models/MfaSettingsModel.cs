namespace ShiftSoftware.ShiftIdentity.Core.Models;

/// <summary>
/// Multi-factor authentication policy (method-agnostic). Settings specific to a single second-factor
/// method live in their own nested model — e.g. <see cref="Totp"/> for authenticator-app codes.
/// </summary>
public class MfaSettingsModel
{
    public bool Enabled { get; set; } = false;
    public bool Mandatory { get; set; } = false;

    /// <summary>TOTP (authenticator-app) method settings.</summary>
    public TotpSettingsModel Totp { get; set; } = new();
}
