using Microsoft.AspNetCore.Authorization;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authorization;

/// <summary>
/// Requires the caller to be authorized for a specific step-up <see cref="AuthPurpose"/> purpose —
/// satisfied by a full internal access token (voluntary, logged-in) or a temporary token whose
/// purpose matches <see cref="Purpose"/> (enforced, pre-login). See <see cref="StepUpAuthorizationHandler"/>.
/// </summary>
public class StepUpRequirement : IAuthorizationRequirement
{
    public AuthPurpose Purpose { get; }

    /// <summary>
    /// When <c>true</c>, a full internal access token also satisfies this requirement (voluntary,
    /// logged-in operations). When <c>false</c>, only a temporary token bound to <see cref="Purpose"/>
    /// is accepted — used for pre-login completion endpoints (e.g. 2FA login) that must never accept
    /// an already-authenticated session.
    /// </summary>
    public bool AllowAccessToken { get; }

    public StepUpRequirement(AuthPurpose purpose, bool allowAccessToken = true)
    {
        Purpose = purpose;
        AllowAccessToken = allowAccessToken;
    }
}
