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
[JsonDerivedType(typeof(MfaChanged), "mfaChanged")]
[JsonDerivedType(typeof(ReturnToLogin), "returnToLogin")]
[JsonDerivedType(typeof(MfaRecoveryCodeIssued), "mfaRecoveryCodeIssued")]
[JsonDerivedType(typeof(SecurityDeliveryRequested), "deliveryRequested")]
[JsonDerivedType(typeof(SecurityLinkOpened), "securityLinkOpened")]
[JsonDerivedType(typeof(ManualPasswordResetIssued), "manualPasswordResetIssued")]
[JsonDerivedType(typeof(EmailVerificationCompleted), "emailVerificationCompleted")]
[JsonDerivedType(typeof(AdminAccountChanged), "adminAccountChanged")]
[JsonDerivedType(typeof(AuthenticatorStatus), "authenticatorStatus")]
public abstract record AuthOutcome;

public sealed record SessionIssued(TokenDTO Session) : AuthOutcome;
public sealed record ChallengeRequired(AuthenticationChallenge Challenge) : AuthOutcome;
public sealed record AuthenticationRefused(AuthenticationFailure Code, PasswordPolicyFailure? PasswordFailure = null) : AuthOutcome;
public sealed record PasswordChanged(AuthOutcome Continuation) : AuthOutcome;
public sealed record OperationCancelled : AuthOutcome;
public sealed record MfaChanged(AuthOutcome Continuation, bool PasswordAlsoChanged = false) : AuthOutcome;
public sealed record ReturnToLogin : AuthOutcome;
public sealed record MfaRecoveryCodeIssued(string Code, DateTimeOffset ExpiresAt) : AuthOutcome;
public sealed record SecurityDeliveryRequested : AuthOutcome;
public sealed record SecurityLinkOpened(string PageHandle, string MaskedTarget, AuthenticationOperationPurpose Purpose, DateTimeOffset ExpiresAt) : AuthOutcome;
public sealed record ManualPasswordResetIssued(string Grant, string MaskedTarget, DateTimeOffset ExpiresAt) : AuthOutcome;
public sealed record EmailVerificationCompleted : AuthOutcome;
/// <summary>An administrator mutation committed, or was already in effect. It never carries a session.</summary>
public sealed record AdminAccountChanged(AdminAccountChange Change, bool Applied, long SecurityVersion, AuthOutcome? Delivery = null) : AuthOutcome;
public enum AdminAccountChange { Password = 1, Username = 2, Email = 3, Active = 4 }
/// <summary>
/// The signed-in account's own authenticator state, read from the authoritative security state. It is a read for
/// the account screens: it proves nothing, issues nothing and never carries a session.
/// </summary>
public sealed record AuthenticatorStatus(bool Enrolled, bool RecoveryRequired) : AuthOutcome;

public enum AuthenticationStep { ExistingMfa, PasswordChange, MfaRecovery, NewMfa, EmailVerification, Password }
public enum AuthenticationFailure
{
    InvalidRequest, InvalidProof, InvalidGrant, StaleOperation, Expired, AttemptsExhausted,
    AccountUnavailable, ClientDenied, Unavailable, InvalidNewPassword, DuplicateIdentifier
}
public enum AuthenticationOperationPurpose { Login = 1, ContactChange = 2, MfaEnrollment = 3, PasswordChange = 4, MfaReplacement = 5, MfaRecovery = 6, PasswordResetEmail = 7, PasswordResetManual = 8, EmailVerify = 9, AppExchange = 10, LegacyRefreshExchange = 11, LegacyMfaExchange = 12 }

public sealed record AuthenticationChallenge(
    AuthenticationStep Step, string? Handle, DateTimeOffset ExpiresAt,
    AuthenticationOperationPurpose Purpose = AuthenticationOperationPurpose.Login,
    NewAuthenticatorSetup? NewAuthenticator = null);

// Sent only after the required proof, over the protected response, and held in component memory.
public sealed record NewAuthenticatorSetup(string Secret, string Uri, string Svg);

public sealed record StartMfaRequest(
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge,
    bool Replace = false);
public sealed record RecoverMfaRequest(
    [property: Required, MaxLength(255)] string Username,
    [property: Required, MaxLength(1024)] string CurrentPassword,
    [property: Required, MaxLength(64)] string RecoveryCode,
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);
public sealed record IssueMfaRecoveryRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    [property: Required, StringLength(200, MinimumLength = 3)] string VerificationReference);

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
