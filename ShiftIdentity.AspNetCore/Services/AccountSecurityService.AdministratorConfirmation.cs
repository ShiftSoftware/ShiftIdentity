using System.Security.Cryptography;
using System.Text;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

internal static partial class AccountSecurityService
{
    internal static Task<AuthOutcome> BeginAdministratorConfirmationAsync(IdentityAdmissionServices services,
        string authorization, StartPasswordChangeRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null || session.Proof.External) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(session.Proof.UserID, null, services.Client, unit =>
        {
            var refusal = ConfirmationSessionRefusal(services, unit, session);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            var deadline = now.AddMinutes(5) < session.ExpiresAt ? now.AddMinutes(5) : session.ExpiresAt;
            var challenge = AdmissionOperations.Create(services, unit, AuthenticationOperationPurpose.AdministratorConfirmation,
                AuthenticationOperationState.AwaitingPassword, request.CodeChallenge, now, deadline, sourceSessionDigest: SessionDigest(authorization));
            return Task.FromResult<AuthOutcome>(challenge);
        }, ct);
    });

    internal static Task<AuthOutcome> ProveAdministratorPasswordAsync(IdentityAdmissionServices services,
        string authorization, AdministratorPasswordProofRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.AdministratorConfirmation, ct);
        if (reference is null || reference.UserID != session.Proof.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        var read = await services.Store.AdmitAsync(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingPassword);
            return Task.FromResult(new SnapshotResult(failure is null ? new(unit.User, unit.Security, unit.Policy) : null, failure));
        }, ct);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        var valid = HashService.VerifyVersionedPassword(request.CurrentPassword, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        services.Observe?.Invoke("AdministratorPasswordProof");
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingPassword);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            if (!SameCredential(snapshot, unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (!valid)
            {
                AdmissionOperations.FailedAttempt(unit, services.Clock.GetUtcNow(), "InvalidPassword");
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            unit.Operation!.PasswordProvenAt = provenAt;
            return Task.FromResult(LocalStep(unit, false) == AuthenticationStep.ExistingMfa
                ? AdmissionOperations.Advance(services, unit.Operation, AuthenticationOperationState.AwaitingMfa)
                : CompleteAdministratorConfirmation(services, unit));
        }, ct);
    });

    internal static Task<AuthOutcome> ProveAdministratorMfaAsync(IdentityAdmissionServices services,
        string authorization, AdministratorMfaProofRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var session = ReadSignedIn(services, authorization);
        if (session is null) return Refuse(AuthenticationFailure.InvalidGrant);
        var reference = await AdmissionOperations.ReadAsync(services, request.Handle, AuthenticationOperationPurpose.AdministratorConfirmation, ct);
        if (reference is null || reference.UserID != session.Proof.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var failure = CheckConfirmation(services, unit, session, authorization, reference, request.CodeVerifier, AuthenticationOperationState.AwaitingMfa);
            if (failure is not null) return Task.FromResult<AuthOutcome>(failure);
            if (unit.Operation!.PasswordProvenAt is null || LocalStep(unit, false) != AuthenticationStep.ExistingMfa)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            failure = AdmissionOperations.VerifyMfa(services, unit, request.Code);
            return Task.FromResult<AuthOutcome>(failure ?? CompleteAdministratorConfirmation(services, unit));
        }, ct);
    });

    private static AuthenticationRefused? ConfirmationSessionRefusal(IdentityAdmissionServices services,
        IdentitySecurityTransaction unit, SignedInContext session) =>
        SignedInRefusal(services, unit, session) ?? (LocalStep(unit, true) is not null
            ? Refuse(AuthenticationFailure.InvalidProof) : null);

    private static AuthenticationRefused? CheckConfirmation(IdentityAdmissionServices services, IdentitySecurityTransaction unit,
        SignedInContext session, string authorization, AdmissionOperations.Reference reference, string verifier, AuthenticationOperationState state)
    {
        if (session.Proof.UserID != reference.UserID || session.Proof.External || unit.Operation?.SourceSessionDigest is not { } digest ||
            !CryptographicOperations.FixedTimeEquals(digest, SessionDigest(authorization))) return Refuse(AuthenticationFailure.InvalidGrant);
        return ConfirmationSessionRefusal(services, unit, session) ?? AdmissionOperations.Check(services, unit, reference, verifier,
            AuthenticationOperationPurpose.AdministratorConfirmation, state);
    }

    private static AuthOutcome CompleteAdministratorConfirmation(IdentityAdmissionServices services, IdentitySecurityTransaction unit)
    {
        var operation = unit.Operation!;
        var now = services.Clock.GetUtcNow();
        var proof = Proof(unit, services, operation.MfaProvenAt is not null, operation.PasswordProvenAt!.Value);
        AdmissionOperations.Finish(operation, now);
        unit.Audit("AdministratorConfirmed", now, operation.ID);
        // Only actual password and applicable MFA proof creates this authentication time. Renewal keeps it.
        return Issue(services, unit, proof, now);
    }

    private static byte[] SessionDigest(string authorization) => SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
}
