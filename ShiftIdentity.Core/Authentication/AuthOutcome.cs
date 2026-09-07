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
[JsonDerivedType(typeof(PasswordChanged), "passwordChanged")]
[JsonDerivedType(typeof(OperationCancelled), "cancelled")]
public abstract record AuthOutcome;

public sealed record SessionIssued(TokenDTO Session) : AuthOutcome;
public sealed record ChallengeRequired(AuthenticationChallenge Challenge) : AuthOutcome;
public sealed record AuthenticationRefused(AuthenticationFailure Code, PasswordPolicyFailure? PasswordFailure = null) : AuthOutcome;
public sealed record PasswordChanged(AuthOutcome Continuation) : AuthOutcome;
public sealed record OperationCancelled : AuthOutcome;

public enum AuthenticationStep { ExistingMfa, PasswordChange, MfaRecovery, NewMfa, EmailVerification, Password }
public enum AuthenticationFailure
{
    InvalidRequest, InvalidProof, InvalidGrant, StaleOperation, Expired, AttemptsExhausted,
    AccountUnavailable, ClientDenied, Unavailable, InvalidNewPassword
}
public enum AuthenticationOperationPurpose { Login = 1, ContactChange = 2, MfaEnrollment = 3, PasswordChange = 4 }

public sealed record AuthenticationChallenge(
    AuthenticationStep Step, string? Handle, DateTimeOffset ExpiresAt,
    AuthenticationOperationPurpose Purpose = AuthenticationOperationPurpose.Login);

public sealed record PasswordLoginRequest(
    [property: Required, MaxLength(255)] string Username,
    [property: Required, MaxLength(1024)] string Password,
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);

public sealed record CompleteMfaRequest(
    [property: Required, RegularExpression(@"^[0-9]{6,8}$")] string Code,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);

public sealed record RenewSessionRequest(
    [property: Required, MaxLength(16384)] string RefreshToken);

public sealed record StartPasswordChangeRequest(
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);
public sealed record PasswordChangeProofRequest(
    [property: Required, MaxLength(1024)] string CurrentPassword,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);
public sealed record CompletePasswordChangeRequest(
    [property: Required, MaxLength(512)] string NewPassword,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);
public sealed record CancelOperationRequest(
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);
