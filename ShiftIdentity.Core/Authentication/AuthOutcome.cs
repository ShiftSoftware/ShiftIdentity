using System;
using System.Collections.Generic;
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
[JsonDerivedType(typeof(ProviderRedirect), "providerRedirect")]
[JsonDerivedType(typeof(ProviderLinksRead), "providerLinks")]
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
public sealed record EmailVerificationCompleted(string? RedirectUrl = null) : AuthOutcome;
/// <summary>An administrator mutation committed, or was already in effect. It never carries a session.</summary>
public sealed record AdminAccountChanged(AdminAccountChange Change, bool Applied, long SecurityVersion, AuthOutcome? Delivery = null) : AuthOutcome;
public enum AdminAccountChange { Password = 1, Username = 2, Email = 3, Active = 4, Mfa = 5 }
/// <summary>
/// The signed-in account's own authenticator state, read from the authoritative security state. It is a read for
/// the account screens: it proves nothing, issues nothing and never carries a session. <see cref="Mandatory"/> is
/// the host's policy: every account must have an authenticator, so no screen offers to turn one off.
/// </summary>
public sealed record AuthenticatorStatus(bool Enrolled, bool RecoveryRequired, bool Mandatory = false) : AuthOutcome;
/// <summary>The browser continues at the sign-in provider. It carries no credential; the session comes after the provider returns.</summary>
public sealed record ProviderRedirect(string Url) : AuthOutcome;
/// <summary>The provider accounts that can sign in to an account. A read for the account screens: it proves and issues nothing.</summary>
public sealed record ProviderLinksRead(IReadOnlyList<ProviderLinkView> Links) : AuthOutcome;
/// <summary>A provider account that can sign in: the email it had when linked, and whether it is a personal account rather than a work or school one.</summary>
public sealed record ProviderLinkView(SignInProvider Provider, string Email, bool PersonalAccount, DateTimeOffset LinkedAt, DateTimeOffset LastUsedAt);

public enum AuthenticationStep { ExistingMfa, PasswordChange, MfaRecovery, NewMfa, EmailVerification, Password }
public enum AuthenticationFailure
{
    InvalidRequest, InvalidProof, InvalidGrant, StaleOperation, Expired, AttemptsExhausted,
    AccountUnavailable, ClientDenied, Unavailable, InvalidNewPassword, DuplicateIdentifier, ReauthenticationRequired,
    /// <summary>No active account has the email address the sign-in provider vouched for.</summary>
    ProviderAccountNotFound,
    /// <summary>The sign-in provider did not vouch for an email address, so no account can be matched.</summary>
    ProviderEmailUnverified
}
public enum AuthenticationOperationPurpose { Login = 1, ContactChange = 2, MfaEnrollment = 3, PasswordChange = 4, MfaReplacement = 5, MfaRecovery = 6, PasswordResetEmail = 7, PasswordResetManual = 8, EmailVerify = 9, AppExchange = 10, LegacyRefreshExchange = 11, LegacyMfaExchange = 12, AdministratorConfirmation = 13, ProviderLogin = 14 }

/// <summary>An external sign-in provider. A provider sign-in reaches only an existing account with the same verified email.</summary>
public enum SignInProvider { Microsoft = 1, Google = 2 }

/// <summary>The sign-in providers this host has turned on, for the login screen. An older API's answer has no Google.</summary>
public sealed record SignInProviders(bool Microsoft, bool Google = false)
{
    /// <summary>The enabled providers in the order the login screen shows them.</summary>
    [JsonIgnore]
    public IReadOnlyList<SignInProvider> Enabled
    {
        get
        {
            var enabled = new List<SignInProvider>(2);
            if (Microsoft) enabled.Add(SignInProvider.Microsoft);
            if (Google) enabled.Add(SignInProvider.Google);
            return enabled;
        }
    }
}

public sealed record StartProviderSignInRequest(
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);

/// <summary>Completes a provider sign-in with the handle the provider's return carried and this browser's verifier.</summary>
public sealed record CompleteProviderSignInRequest(
    [property: Required, MaxLength(2048)] string Handle,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);

/// <summary>A definitive refusal before an administrator mutation. Other errors never authorize a replay.</summary>
public static class AdministratorAuthentication
{
    public const string RequiredMessage = "IdentityReauthenticationRequired";
    public const int DefaultGracePeriodSeconds = 20 * 60 * 60;
}

public sealed record AdministratorPasswordProofRequest(
    [property: Required, MaxLength(2048)] string Handle,
    [property: Required, MaxLength(1024)] string CurrentPassword,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);
public sealed record AdministratorMfaProofRequest(
    [property: Required, MaxLength(2048)] string Handle,
    [property: Required, RegularExpression(@"^[0-9]{6,8}$")] string Code,
    [property: Required, StringLength(128, MinimumLength = 43)] string CodeVerifier);

public sealed record AuthenticationChallenge(
    AuthenticationStep Step, string? Handle, DateTimeOffset ExpiresAt,
    AuthenticationOperationPurpose Purpose = AuthenticationOperationPurpose.Login,
    NewAuthenticatorSetup? NewAuthenticator = null);

// Sent only after the required proof, over the protected response, and held in component memory.
public sealed record NewAuthenticatorSetup(string Secret, string Uri, string Svg);

public sealed record StartMfaRequest(
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge,
    bool Replace = false);
/// <summary>
/// The signed-in account turns off its own authenticator, where MFA is optional, with a current code from it. The
/// answer carries a fresh session for this device; every other session of the account ends.
/// </summary>
public sealed record TurnOffMfaRequest(
    [property: Required, RegularExpression(@"^[0-9]{6,8}$")] string Code);
public sealed record RecoverMfaRequest(
    [property: Required, MaxLength(255)] string Username,
    [property: Required, MaxLength(1024)] string CurrentPassword,
    [property: Required, MaxLength(64)] string RecoveryCode,
    [property: Required, StringLength(43, MinimumLength = 43)] string CodeChallenge);
/// <summary>
/// Names the target by its numeric ID or by the encoded key a dashboard form holds, not both. The endpoint decodes
/// the key before the request is validated, so a request that names neither, or both, is refused.
/// </summary>
public sealed record IssueMfaRecoveryRequest(
    [property: Range(1, long.MaxValue)] long UserID,
    [property: Required, StringLength(200, MinimumLength = 3)] string VerificationReference,
    string? UserKey = null);

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
