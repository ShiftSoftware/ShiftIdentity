using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Core.Authentication;

/// <summary>A session and a restricted operation are deliberately different wire types.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(SessionIssued), "session")]
[JsonDerivedType(typeof(ChallengeRequired), "challenge")]
[JsonDerivedType(typeof(AuthenticationRefused), "refused")]
public abstract record AuthOutcome;

public sealed record SessionIssued(TokenDTO Session) : AuthOutcome;
public sealed record ChallengeRequired(AuthenticationChallenge Challenge) : AuthOutcome;
public sealed record AuthenticationRefused(AuthenticationFailure Code) : AuthOutcome;

public enum AuthenticationStep { ExistingMfa, PasswordChange, MfaRecovery, NewMfa, EmailVerification }
public enum AuthenticationFailure
{
    InvalidRequest, InvalidProof, InvalidGrant, StaleOperation, Expired, AttemptsExhausted,
    AccountUnavailable, ClientDenied, Unavailable
}
public enum AuthenticationOperationPurpose { Login = 1, ContactChange = 2, MfaEnrollment = 3 }

public sealed record AuthenticationChallenge(
    AuthenticationStep Step, string? Handle, DateTimeOffset ExpiresAt);

public sealed record PasswordLoginRequest(
    [property: Required, MaxLength(255)] string Username,
    [property: Required, MaxLength(1024)] string Password,
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);

public sealed record CompleteMfaRequest(
    [property: Required, RegularExpression(@"^[0-9]{6,8}$")] string Code,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);

public sealed record RenewSessionRequest(
    [property: Required, MaxLength(16384)] string RefreshToken);
