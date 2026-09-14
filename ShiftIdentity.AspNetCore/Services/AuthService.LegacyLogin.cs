using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

// Restricted TokenDTOs belong only to the deployed envelope, never to the v2 SessionIssued wire outcome.
internal sealed record LegacyLoginStepIssued(TokenDTO Token) : AuthOutcome;

public partial class AuthService
{
    internal static Task<AuthOutcome> BeginCompatibleLoginAsync(IdentityAdmissionServices services, LoginDTO request,
        ShiftIdentityConfiguration configuration, CancellationToken ct) => AtBoundary(async () =>
    {
        if (services.Client.External || request?.Username is not { Length: > 0 and <= 255 } ||
            request.Password is not { Length: > 0 and <= 255 }) return Refuse(AuthenticationFailure.InvalidRequest);
        var started = services.Clock.GetUtcNow();
        // Preserve the deployed lookup. Username trimming remains the staged UI/endpoint's separate contract.
        var snapshot = await services.Store.ReadProofAsync(request.Username, services.Client, ct);
        if (snapshot is null || snapshot.User.IsDeleted) return Refuse(AuthenticationFailure.InvalidProof);
        var valid = HashService.VerifyVersionedPassword(request.Password, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        var upgrade = valid && VersionedPasswordHash.NeedsUpgrade(snapshot.User.PasswordHash)
            ? HashService.GenerateVersionedHash(request.Password) : null;
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var challenge = WebEncoders.Base64UrlEncode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier)));
        services.Observe?.Invoke("LegacyLoginProof");
        var outcome = await services.Store.AdmitAsync<AuthOutcome>(snapshot.User.ID, null, services.Client, unit =>
        {
            services.Observe?.Invoke("AdmissionLock");
            var now = services.Clock.GetUtcNow();
            if (unit.User.IsDeleted) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            if (unit.Security.SecurityVersion != snapshot.Security.SecurityVersion || unit.Policy.Revision != snapshot.Policy.Revision ||
                unit.Security.FactorGeneration != snapshot.Security.FactorGeneration || unit.User.Username != snapshot.User.Username ||
                !CryptographicOperations.FixedTimeEquals(unit.User.PasswordHash, snapshot.User.PasswordHash) ||
                !CryptographicOperations.FixedTimeEquals(unit.User.Salt, snapshot.User.Salt))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (unit.Policy.Revision != services.Options.PolicyRevision)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Unavailable));
            if (now < started || now >= started.AddMinutes(5)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            // Legacy failure ordering is deliberate: a wrong password counts even on inactive/locked accounts.
            if (!valid)
            {
                unit.User.LoginAttempts++;
                if (unit.User.LoginAttempts >= configuration.Security.LoginAttemptsForLockDown)
                {
                    unit.User.LoginAttempts = 0;
                    unit.User.LockDownUntil = now.UtcDateTime.AddMinutes(configuration.Security.LockDownInMinutes);
                }
                FailedProof(unit.Security, now);
                unit.Audit("InvalidPassword", now);
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            if (!unit.User.IsActive) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AccountUnavailable));
            if (unit.User.LockDownUntil > now.UtcDateTime || BudgetExhausted(unit.Security, now))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
            unit.User.LoginAttempts = 0;
            unit.User.LockDownUntil = null;
            unit.User.UserLog ??= new Data.Entities.UserLog();
            unit.User.UserLog.LastSeen = now;
            if (upgrade is not null) { unit.User.PasswordHash = upgrade.PasswordHash; unit.User.Salt = upgrade.Salt; }
            unit.Audit("LegacyLoginPasswordProven", now);
            var seconds = configuration.TemporaryTokenSettings.ExpireSeconds;
            if (seconds <= 0) throw new IdentitySecurityUnavailableException("Temporary login lifetime is missing.");
            var next = ContinuePasswordLogin(services, unit, challenge, started, provenAt,
                started.AddSeconds(Math.Min(seconds, 300)));
            if (next is ChallengeRequired step)
                return Task.FromResult<AuthOutcome>(step.Challenge.Handle is not null
                    ? new LegacyLoginStepIssued(new LegacyLoginTokenCodec(services).Issue(unit, step.Challenge, verifier))
                    : step);
            return Task.FromResult(next);
        }, ct);
        return outcome;
    });

    // One decision function for both HTTP contracts. All local restrictions use the locked staged policy/factor state.
    private static AuthOutcome ContinuePasswordLogin(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        string challenge, DateTimeOffset started, DateTimeOffset provenAt, DateTimeOffset? deadline = null)
    {
        var next = LocalStep(unit, false);
        return next switch
        {
            AuthenticationStep.PasswordChange => AdmissionOperations.Create(services, unit,
                AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingNewPassword,
                challenge, started, deadline ?? started.AddMinutes(5), PasswordChangeOrigin.RequiredLogin, provenAt),
            AuthenticationStep.NewMfa => AdmissionOperations.Create(services, unit,
                AuthenticationOperationPurpose.MfaEnrollment, AuthenticationOperationState.AwaitingNewFactor,
                challenge, started, deadline ?? started.AddMinutes(10), passwordProvenAt: provenAt, prepareNewFactor: true),
            AuthenticationStep.ExistingMfa => AdmissionOperations.Create(services, unit,
                AuthenticationOperationPurpose.Login, AuthenticationOperationState.AwaitingMfa,
                challenge, started, deadline ?? started.AddMinutes(5), passwordProvenAt: provenAt),
            { } step => Restricted(step, services.Clock.GetUtcNow()),
            null => Issue(services, unit, Proof(unit, services, false, provenAt), services.Clock.GetUtcNow())
        };
    }

    internal static Task<AuthOutcome> CompatibleLoginOutcomeAsync(IdentityAdmissionServices services, AuthOutcome outcome,
        string verifier, CancellationToken ct) => AtBoundary(async () =>
    {
        outcome = outcome switch { PasswordChanged changed => changed.Continuation, MfaChanged changed => changed.Continuation, _ => outcome };
        if (outcome is not ChallengeRequired required) return outcome;
        var challenge = required.Challenge;
        if (challenge.Handle is null || LegacyLoginTokenCodec.Flow(challenge.Step) == Core.Enums.AuthPurpose.None)
            // Recovery/email gates are accepted new restrictions. The deployed enum cannot represent those journeys.
            return required;
        var reference = await AdmissionOperations.ReadAsync(services, challenge.Handle, challenge.Purpose, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var state = challenge.Step switch
            {
                AuthenticationStep.ExistingMfa => AuthenticationOperationState.AwaitingMfa,
                AuthenticationStep.NewMfa => AuthenticationOperationState.AwaitingNewFactor,
                _ => AuthenticationOperationState.AwaitingNewPassword
            };
            var refusal = AdmissionOperations.Check(services, unit, reference, verifier, challenge.Purpose, state);
            return Task.FromResult<AuthOutcome>(refusal is not null ? refusal :
                new LegacyLoginStepIssued(new LegacyLoginTokenCodec(services).Issue(unit, challenge, verifier)));
        }, ct);
    });

    internal static TokenDTO? CompatibleLoginToken(AuthOutcome outcome) => outcome switch
    { SessionIssued issued => issued.Session, LegacyLoginStepIssued issued => issued.Token, _ => null };

    internal LoginResultModel CompatibleLoginResult(AuthOutcome outcome)
    {
        if (CompatibleLoginToken(outcome) is { } token) return new(token);
        return outcome switch
        {
            ChallengeRequired { Challenge.Step: AuthenticationStep.EmailVerification } => new(LoginResultEnum.PasswordIncorrect,
                "Verify your saved email address before signing in."),
            ChallengeRequired { Challenge.Step: AuthenticationStep.MfaRecovery } => new(LoginResultEnum.PasswordIncorrect,
                "Authenticator recovery is required. Contact your administrator."),
            AuthenticationRefused { Code: AuthenticationFailure.AccountUnavailable } => new(LoginResultEnum.UserDeactive, Loc["The user is deactivated"]),
            AuthenticationRefused { Code: AuthenticationFailure.AttemptsExhausted } => new(LoginResultEnum.UserLockDown,
                Loc["User is lockdown for {0} minutes", shiftIdentityConfigurations.Security.LockDownInMinutes]),
            _ => new(LoginResultEnum.PasswordIncorrect, Loc["Username or password is incorrect"])
        };
    }
}
