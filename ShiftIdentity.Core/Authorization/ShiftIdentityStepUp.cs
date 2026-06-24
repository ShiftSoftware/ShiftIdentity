using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.Authorization;

/// <summary>
/// Authentication scheme names used by ShiftIdentity.
/// </summary>
public static class ShiftIdentityAuthenticationSchemes
{
    /// <summary>
    /// Scheme that authenticates short-lived, purpose-bound <em>temporary</em> tokens — the ones
    /// issued before login completes for an enforced flow (e.g. forced change-password or forced
    /// two-factor enrollment). These are signed with the refresh-token key, not the access-token key,
    /// so they need their own scheme alongside the default access-token bearer scheme.
    /// </summary>
    public const string TemporaryToken = "ShiftIdentity.TemporaryToken";
}

/// <summary>
/// "Step-up" authorization lets a single endpoint serve two callers:
/// <list type="bullet">
///   <item>a logged-in user acting voluntarily (full access token), and</item>
///   <item>a user in an enforced pre-login flow holding only a purpose-bound temporary token.</item>
/// </list>
/// The endpoint declares the <see cref="AuthPurpose"/> purpose it serves; authorization succeeds
/// for a full internal access token, or for a temporary token whose purpose matches.
/// </summary>
public static class StepUpPolicy
{
    public const string Prefix = "ShiftIdentity.StepUp:";

    /// <summary>
    /// The policy name for a given step-up purpose.
    /// <paramref name="allowAccessToken"/> controls whether a full internal access token also satisfies
    /// it (<c>true</c> — for sensitive operations a logged-in user may perform voluntarily) or whether
    /// only the matching purpose-bound temporary token is accepted (<c>false</c> — for pre-login
    /// completion endpoints such as the 2FA login step, where there is no "already logged in" caller).
    /// </summary>
    public static string For(AuthPurpose purpose, bool allowAccessToken = true) =>
        $"{Prefix}{purpose}:{(allowAccessToken ? "AccessOrPurpose" : "PurposeOnly")}";
}
