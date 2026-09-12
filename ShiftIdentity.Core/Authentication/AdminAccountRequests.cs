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
