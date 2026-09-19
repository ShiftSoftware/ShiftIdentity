using System;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

// The host fixes the client and return route. Requesters cannot supply a delivery destination.
public sealed record RequestSecurityEmail([property: Required, MaxLength(255)] string Identifier);
// Use either the numeric authority ID or the deployed dashboard's encoded key; never both.
public sealed record AdminPasswordResetRequest(long UserID = 0, bool Manual = false, string? UserKey = null);
public sealed record AdminEmailVerificationRequest(long UserID = 0, string? UserKey = null);
public sealed record OpenSecurityLinkRequest([property: Required, MaxLength(512)] string Grant, AuthenticationOperationPurpose Purpose);
public sealed record CompletePasswordResetRequest([property: Required, MaxLength(4096)] string PageHandle,
    [property: Required, MaxLength(512)] string NewPassword);
public sealed record CompleteEmailVerificationRequest([property: Required, MaxLength(4096)] string PageHandle);
