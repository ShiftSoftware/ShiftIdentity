using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

// Administrator account mutations. The actor is the bearer session and the host fixes the client; neither can be
// supplied in the body. Every applied change increments the target's SecurityVersion and never issues a session.
public sealed record AdminSetPasswordRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    [property: Required, MaxLength(512)] string NewPassword,
    bool RequireChangeAtNextLogin = true);
public sealed record AdminUsernameChangeRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    [property: Required, MaxLength(255)] string Username);
public sealed record AdminEmailChangeRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    [property: MaxLength(255)] string? Email,
    bool SendVerification = true);
public sealed record AdminAccountStatusRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    bool Active);
/// <summary>
/// Turns off the target's MFA where it is optional: the authenticator, a pending recovery and its code are removed,
/// and the user signs in with the password only. It is guarded like an MFA recovery code (the Manage MFA Recovery
/// permission, a recent operator sign-in and a recorded identity-check note). The target is named by its numeric ID
/// or by the encoded key a dashboard form holds, not both.
/// </summary>
public sealed record AdminMfaTurnOffRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    [property: Required, StringLength(200, MinimumLength = 3)] string VerificationReference,
    string? UserKey = null);
