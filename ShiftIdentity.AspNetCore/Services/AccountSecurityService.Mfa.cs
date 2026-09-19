using System.Security.Cryptography;
using OtpNet;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

internal static partial class AccountSecurityService
{
    /// <summary>
    /// The signed-in account's authenticator state for the account screens, read under the same admission lock and
    /// current-version, policy, factor and subject checks as every other bearer route. It transitions nothing.
    /// </summary>
    internal static Task<AuthOutcome> ReadAuthenticatorAsync(IdentityAdmissionServices services, string? authorization,
        CancellationToken ct) => AtBoundary(async () =>
    {
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(signedIn.Proof.UserID, null, services.Client, unit =>
        {
            var refusal = SignedInRefusal(services, unit, signedIn);
            return Task.FromResult<AuthOutcome>(refusal is not null ? refusal :
                new AuthenticatorStatus(unit.Security.ProtectedTotpSecret is not null, unit.Security.LocalMfaRecoveryRequired));
        }, ct);
    });

    internal static Task<AuthOutcome> BeginMfaAsync(IdentityAdmissionServices services, string? authorization,
        StartMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(signedIn.Proof.UserID, null, services.Client, unit =>
        {
            var refusal = SignedInRefusal(services, unit, signedIn);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            if (unit.Security.LocalMfaRecoveryRequired) return Task.FromResult<AuthOutcome>(Restricted(AuthenticationStep.MfaRecovery, now));
            if (unit.User.RequireChangePassword) return Task.FromResult<AuthOutcome>(Restricted(AuthenticationStep.PasswordChange, now));
            if (request.Replace != (unit.Security.ProtectedTotpSecret is not null))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            return Task.FromResult<AuthOutcome>(AdmissionOperations.Create(services, unit,
                request.Replace ? AuthenticationOperationPurpose.MfaReplacement : AuthenticationOperationPurpose.MfaEnrollment,
                request.Replace ? AuthenticationOperationState.AwaitingMfa : AuthenticationOperationState.AwaitingPassword,
                request.CodeChallenge, now, Earlier(now.AddMinutes(10), signedIn.ExpiresAt)));
        }, ct);
    });

    internal static Task<AuthOutcome> ProveMfaPasswordAsync(IdentityAdmissionServices services, string? handle,
        PasswordChangeProofRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, handle, AuthenticationOperationPurpose.MfaEnrollment, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var read = await ReadSnapshot(services, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingPassword, ct,
            AuthenticationOperationPurpose.MfaEnrollment);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        var valid = HashService.VerifyVersionedPassword(request.CurrentPassword, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        services.Observe?.Invoke("MfaPasswordProof");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.MfaEnrollment, AuthenticationOperationState.AwaitingPassword);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!SameCredential(snapshot, unit) || unit.Security.ProtectedTotpSecret is not null || unit.Security.LocalMfaRecoveryRequired)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (services.Clock.GetUtcNow() >= provenAt.AddMinutes(5))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            if (!valid)
            {
                AdmissionOperations.FailedAttempt(unit, services.Clock.GetUtcNow(), "InvalidPassword");
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            unit.Operation!.PasswordProvenAt = provenAt;
            return Task.FromResult<AuthOutcome>(PrepareNewFactor(services, unit));
        }, ct);
    });

    internal static Task<AuthOutcome> ProveExistingFactorAsync(IdentityAdmissionServices services, string? handle,
        CompleteMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, handle, AuthenticationOperationPurpose.MfaReplacement, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.MfaReplacement, AuthenticationOperationState.AwaitingMfa);
            refusal ??= AdmissionOperations.VerifyMfa(services, unit, request.Code, requireEnabled: false);
            return Task.FromResult<AuthOutcome>(refusal is not null ? refusal : PrepareNewFactor(services, unit));
        }, ct);
    });

    internal static Task<AuthOutcome> ConfirmNewFactorAsync(IdentityAdmissionServices services, string? handle,
        CompleteMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAnyAsync(services, handle, ct, AuthenticationOperationPurpose.MfaEnrollment,
            AuthenticationOperationPurpose.MfaReplacement, AuthenticationOperationPurpose.MfaRecovery, AuthenticationOperationPurpose.PasswordChange);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier, reference.Purpose,
                AuthenticationOperationState.AwaitingNewFactor);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var op = unit.Operation!;
            var replacing = op.Purpose == AuthenticationOperationPurpose.MfaReplacement;
            var recovering = op.Purpose == AuthenticationOperationPurpose.MfaRecovery;
            if (op.ProtectedPendingTotpSecret is null || replacing != (unit.Security.ProtectedTotpSecret is not null) ||
                recovering != unit.Security.LocalMfaRecoveryRequired ||
                (replacing ? op.MfaProvenAt is null : op.PasswordProvenAt is null) ||
                (op.Purpose == AuthenticationOperationPurpose.PasswordChange && (op.PendingPasswordHash is null || op.PendingPasswordSalt is null)))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var secret = MfaMaterial.ReadPending(services, op);
            try
            {
                var now = services.Clock.GetUtcNow();
                var totp = new Totp(secret, unit.Policy.TotpPeriodSeconds, totpSize: unit.Policy.TotpDigits);
                if (!totp.VerifyTotp(now.UtcDateTime, request.Code, out var step,
                    new VerificationWindow(unit.Policy.TotpWindowPast, unit.Policy.TotpWindowFuture)))
                {
                    AdmissionOperations.FailedAttempt(unit, now, "InvalidNewMfa");
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
                }
                var authenticatedAt = replacing ? op.MfaProvenAt!.Value : op.PasswordProvenAt!.Value;
                MfaMaterial.Activate(services, unit.Security, secret, step);
                services.Observe?.Invoke("MfaMutation");
                unit.Security.LocalMfaRecoveryRequired = false;
                op.MfaProvenAt = now;
                if (op.Purpose == AuthenticationOperationPurpose.PasswordChange)
                {
                    var changed = (PasswordChanged)ApplyPasswordChange(services, unit, op.PendingPasswordHash!, op.PendingPasswordSalt!, now);
                    unit.Audit("MfaEnrolled", now, op.ID);
                    return Task.FromResult<AuthOutcome>(new MfaChanged(changed.Continuation, PasswordAlsoChanged: true));
                }
                unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
                AdmissionOperations.Finish(op, now);
                unit.Audit(recovering ? "MfaRecovered" : replacing ? "MfaReplaced" : "MfaEnrolled", now, op.ID);
                if (recovering)
                {
                    unit.Security.MfaRecoveryOperationID = null;
                    return Task.FromResult<AuthOutcome>(new MfaChanged(new ReturnToLogin()));
                }
                var remaining = LocalStep(unit, true);
                return Task.FromResult<AuthOutcome>(new MfaChanged(remaining is { } next ? Restricted(next, now)
                    : Issue(services, unit, Proof(unit, services, true, authenticatedAt), now)));
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
        }, ct);
    });

    private static ChallengeRequired PrepareNewFactor(IdentityAdmissionServices services, IdentitySecurityTransaction unit) =>
        AdmissionOperations.Advance(services, unit.Operation!, AuthenticationOperationState.AwaitingNewFactor,
            MfaMaterial.Prepare(services, unit, unit.Operation!));

    private static DateTimeOffset Earlier(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
