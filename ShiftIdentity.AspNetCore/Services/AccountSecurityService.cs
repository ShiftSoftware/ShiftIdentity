using System.Security.Cryptography;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

/// <summary>Staged account mutations, using the same locked admission and proof rules as login.</summary>
internal static partial class AccountSecurityService
{
    internal static Task<AuthOutcome> BeginPasswordChangeAsync(IdentityAdmissionServices services, string? authorization,
        StartPasswordChangeRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var proof = signedIn.Proof;
        return await services.Store.AdmitAsync<AuthOutcome>(proof.UserID, null, services.Client, unit =>
        {
            var refusal = SignedInRefusal(services, unit, signedIn);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            var deadline = now.AddMinutes(5) < signedIn.ExpiresAt ? now.AddMinutes(5) : signedIn.ExpiresAt;
            // Existing access/MFA claims establish context only. Fresh password proof follows this operation's creation.
            return Task.FromResult<AuthOutcome>(AdmissionOperations.Create(services, unit,
                AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingPassword,
                request.CodeChallenge, now, deadline, PasswordChangeOrigin.Voluntary));
        }, ct);
    });

    internal static Task<AuthOutcome> ProvePasswordAsync(IdentityAdmissionServices services, string? handle,
        PasswordChangeProofRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, handle, AuthenticationOperationPurpose.PasswordChange, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var read = await ReadSnapshot(services, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingPassword, ct);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        var validPassword = HashService.VerifyVersionedPassword(request.CurrentPassword, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        var upgrade = validPassword && VersionedPasswordHash.NeedsUpgrade(snapshot.User.PasswordHash)
            ? HashService.GenerateVersionedHash(request.CurrentPassword) : null;
        services.Observe?.Invoke("PasswordChangeProof");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingPassword);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!SameCredential(snapshot, unit) || unit.Operation!.PasswordChangeOrigin != PasswordChangeOrigin.Voluntary)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var now = services.Clock.GetUtcNow();
            if (!validPassword)
            {
                AdmissionOperations.FailedAttempt(unit, now, "InvalidPassword");
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            unit.Operation.PasswordProvenAt = provenAt;
            if (upgrade is not null) { unit.User.PasswordHash = upgrade.PasswordHash; unit.User.Salt = upgrade.Salt; }
            var next = LocalStep(unit, false, ignorePasswordChange: true);
            if (next is AuthenticationStep.MfaRecovery)
                return Task.FromResult<AuthOutcome>(StopRestricted(unit, next.Value, now));
            unit.Audit("PasswordChangePasswordProven", now, reference.ID);
            return Task.FromResult<AuthOutcome>(AdmissionOperations.Advance(services, unit.Operation,
                next == AuthenticationStep.ExistingMfa ? AuthenticationOperationState.AwaitingMfa : AuthenticationOperationState.AwaitingNewPassword));
        }, ct);
    });

    internal static Task<AuthOutcome> CompletePasswordChangeAsync(IdentityAdmissionServices services, string? handle,
        CompletePasswordChangeRequest request, CancellationToken ct, string? legacyCurrentPassword = null) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, handle, AuthenticationOperationPurpose.PasswordChange, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var read = await ReadSnapshot(services, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingNewPassword, ct);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        // The deployed forced-change form re-proves CurrentPassword. Bind that proof to the same snapshot used
        // for preparing the new hash; a concurrent change can never upgrade it to the current credential.
        if (legacyCurrentPassword is not null && !HashService.VerifyVersionedPassword(legacyCurrentPassword, snapshot.User.Salt, snapshot.User.PasswordHash))
            return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
            {
                var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                    AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingNewPassword);
                if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
                if (!SameCredential(snapshot, unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
                AdmissionOperations.FailedAttempt(unit, services.Clock.GetUtcNow(), "InvalidPassword");
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }, ct);
        var failure = services.PasswordPolicy.Validate(request.NewPassword, snapshot.User.Username);
        if (failure is not null) return new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, failure);
        if (HashService.VerifyVersionedPassword(request.NewPassword, snapshot.User.Salt, snapshot.User.PasswordHash))
            return new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, PasswordPolicyFailure.SameAsCurrent);
        // Expensive KDF runs outside the SQL lock; the original credential and all bindings are checked again below.
        var candidate = HashService.GenerateVersionedHash(request.NewPassword);
        services.Observe?.Invoke("PasswordPrepared");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingNewPassword);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!SameCredential(snapshot, unit) || unit.Operation!.PasswordProvenAt is null)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var op = unit.Operation;
            var now = services.Clock.GetUtcNow();
            var next = LocalStep(unit, op.MfaProvenAt is not null, ignorePasswordChange: true);
            if (next is AuthenticationStep.MfaRecovery)
                return Task.FromResult<AuthOutcome>(StopRestricted(unit, next.Value, now));
            if (next is AuthenticationStep.ExistingMfa or AuthenticationStep.NewMfa)
            {
                op.PendingPasswordHash = candidate.PasswordHash;
                op.PendingPasswordSalt = candidate.Salt;
                if (next == AuthenticationStep.NewMfa) return Task.FromResult<AuthOutcome>(PrepareNewFactor(services, unit));
                return Task.FromResult<AuthOutcome>(AdmissionOperations.Advance(services, op, AuthenticationOperationState.AwaitingMfa));
            }
            return Task.FromResult(ApplyPasswordChange(services, unit, candidate.PasswordHash, candidate.Salt, now));
        }, ct);
    });

    internal static Task<AuthOutcome> CompleteMfaAsync(IdentityAdmissionServices services, string? handle,
        CompleteMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, handle, AuthenticationOperationPurpose.PasswordChange, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingMfa);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var op = unit.Operation!;
            if (op.PasswordProvenAt is null || LocalStep(unit, false, ignorePasswordChange: true) != AuthenticationStep.ExistingMfa)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            refusal = AdmissionOperations.VerifyMfa(services, unit, request.Code);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            return Task.FromResult(op.PendingPasswordHash is { } hash && op.PendingPasswordSalt is { } salt
                ? ApplyPasswordChange(services, unit, hash, salt, services.Clock.GetUtcNow())
                : AdmissionOperations.Advance(services, op, AuthenticationOperationState.AwaitingNewPassword));
        }, ct);
    });

    internal static Task<AuthOutcome> CancelAsync(IdentityAdmissionServices services, string? handle,
        CancelOperationRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAnyAsync(services, handle, ct,
            AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationPurpose.Login,
            AuthenticationOperationPurpose.MfaEnrollment, AuthenticationOperationPurpose.MfaReplacement, AuthenticationOperationPurpose.MfaRecovery,
            AuthenticationOperationPurpose.AdministratorConfirmation);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier, reference.Purpose,
                AuthenticationOperationState.AwaitingPassword, AuthenticationOperationState.AwaitingNewPassword, AuthenticationOperationState.AwaitingMfa,
                AuthenticationOperationState.AwaitingNewFactor);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            AdmissionOperations.Finish(unit.Operation!, services.Clock.GetUtcNow(), cancelled: true);
            unit.Audit("OperationCancelled", services.Clock.GetUtcNow(), reference.ID);
            return Task.FromResult<AuthOutcome>(new OperationCancelled());
        }, ct);
    });

    private static AuthOutcome ApplyPasswordChange(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        byte[] hash, byte[] salt, DateTimeOffset now)
    {
        var op = unit.Operation!;
        unit.User.PasswordHash = hash;
        unit.User.Salt = salt;
        unit.User.RequireChangePassword = false;
        unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
        services.Observe?.Invoke("PasswordMutation");
        AdmissionOperations.Finish(op, now);
        unit.Audit("PasswordChanged", now, op.ID);
        var remaining = LocalStep(unit, op.MfaProvenAt is not null);
        AuthOutcome continuation = remaining is { } step
            ? RestrictedForOperation(op, step)
            : Issue(services, unit, Proof(unit, services, op.MfaProvenAt is not null, op.PasswordProvenAt!.Value), now);
        return new PasswordChanged(continuation);
    }

    private static ChallengeRequired StopRestricted(IdentitySecurityTransaction unit, AuthenticationStep step, DateTimeOffset now)
    {
        AdmissionOperations.Finish(unit.Operation!, now, cancelled: true);
        return RestrictedForOperation(unit.Operation!, step);
    }

    private static ChallengeRequired RestrictedForOperation(AuthenticationOperation op, AuthenticationStep step) =>
        new(new(step, null, op.ExpiresAt, op.Purpose));

    private static bool SameCredential(IdentityProofSnapshot snapshot, IdentitySecurityTransaction unit) =>
        snapshot.Security.SecurityVersion == unit.Security.SecurityVersion &&
        CryptographicOperations.FixedTimeEquals(snapshot.User.PasswordHash, unit.User.PasswordHash) &&
        CryptographicOperations.FixedTimeEquals(snapshot.User.Salt, unit.User.Salt);

    private sealed record SnapshotResult(IdentityProofSnapshot? Snapshot, AuthenticationRefused? Failure);

    private static Task<SnapshotResult> ReadSnapshot(IdentityAdmissionServices services, AdmissionOperations.Reference reference,
        string verifier, AuthenticationOperationState state, CancellationToken ct, AuthenticationOperationPurpose purpose = AuthenticationOperationPurpose.PasswordChange) =>
        services.Store.AdmitAsync(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = AdmissionOperations.Check(services, unit, reference, verifier, purpose, state);
            return Task.FromResult(new SnapshotResult(failure is null ? new(unit.User, unit.Security, unit.Policy) : null, failure));
        }, ct);
}
