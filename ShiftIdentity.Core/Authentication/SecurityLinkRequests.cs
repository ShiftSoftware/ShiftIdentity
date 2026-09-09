using System;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

// The host fixes the client and return route. Requesters cannot supply a delivery destination.
public sealed record RequestSecurityEmail([property: Required, MaxLength(255)] string Identifier);
public sealed record AdminPasswordResetRequest([property: Range(1, long.MaxValue)] long UserID, bool Manual = false);
public sealed record AdminEmailVerificationRequest([property: Range(1, long.MaxValue)] long UserID);
public sealed record OpenSecurityLinkRequest([property: Required, MaxLength(512)] string Grant, AuthenticationOperationPurpose Purpose);
public sealed record CompletePasswordResetRequest([property: Required, MaxLength(4096)] string PageHandle,
    [property: Required, MaxLength(512)] string NewPassword);
public sealed record CompleteEmailVerificationRequest([property: Required, MaxLength(4096)] string PageHandle);
