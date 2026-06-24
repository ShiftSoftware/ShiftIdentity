namespace ShiftSoftware.ShiftIdentity.Core.Models;

/// <summary>
/// Settings for the TOTP (authenticator-app) second-factor method. Nested under <see cref="MfaSettingsModel.Totp"/>.
/// </summary>
public class TotpSettingsModel
{
    /// <summary>
    /// The name displayed on the user's authenticator app.
    /// </summary>
    public string? IssuerName { get; set; }

    public int VerificationWindowPast { get; set; } = 1;
    public int VerificationWindowFuture { get; set; } = 1;

    /// <summary>
    /// Don't change this value if there are enrolled users.
    /// </summary>
    public int Digits { get; set; } = 6;

    /// <summary>
    /// Don't change this value if there are enrolled users.
    /// </summary>
    public int Period { get; set; } = 30;
}
