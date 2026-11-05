using System;
using System.ComponentModel.DataAnnotations;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

// The host fixes the client and return route. Requesters cannot supply a delivery destination.
// Language is the culture of the app that asks, such as "ru". The emailed link carries it, so that the link's page opens
// in the same language. A missing or malformed value is ignored, and the link then names no language.
public sealed record RequestSecurityEmail([property: Required, MaxLength(255)] string Identifier, string? Language = null);
// Use either the numeric authority ID or the deployed dashboard's encoded key; never both.
// On the requests below, Language works the same way. The recipient's own language is not known, so an administrator's
// request names the language the administrator is using.
public sealed record AdminPasswordResetRequest(long UserID = 0, bool Manual = false, string? UserKey = null, string? Language = null);
public sealed record AdminEmailVerificationRequest(long UserID = 0, string? UserKey = null, string? Language = null);
public sealed record RequestCurrentEmailVerification(string? Language = null);
public sealed record OpenSecurityLinkRequest([property: Required, MaxLength(512)] string Grant, AuthenticationOperationPurpose Purpose);
public sealed record CompletePasswordResetRequest([property: Required, MaxLength(4096)] string PageHandle,
    [property: Required, MaxLength(512)] string NewPassword);
public sealed record CompleteEmailVerificationRequest([property: Required, MaxLength(4096)] string PageHandle);
