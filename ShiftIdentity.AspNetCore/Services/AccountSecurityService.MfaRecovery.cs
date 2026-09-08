using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

internal static partial class AccountSecurityService
{
    internal static Task<AuthOutcome> IssueMfaRecoveryAsync(IdentityAdmissionServices services, string? authorization,
        IssueMfaRecoveryRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || string.IsNullOrWhiteSpace(request.VerificationReference) || request.VerificationReference.Any(char.IsControl))
            return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (signedIn.Proof.UserID == request.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        services.Observe?.Invoke("MfaRecoveryAdminProof");
        return await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = SignedInRefusal(services, actor, signedIn);
            refusal ??= CommonRefusal(services, unit, unit.Security.SecurityVersion, unit.Policy.Revision);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            if (now >= signedIn.Proof.AuthenticatedAt.AddMinutes(5) || LocalStep(actor, signedIn.Proof.MfaSatisfied) is not null)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            var trees = actor.User.AccessTrees.Select(x => x.AccessTree.Tree).ToList();
            if (!string.IsNullOrWhiteSpace(actor.User.AccessTree)) trees.Add(actor.User.AccessTree);
            if (!new TypeAuthContext(trees, typeof(ShiftIdentityActions)).CanAccess(ShiftIdentityActions.ManageMfaRecovery))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            foreach (var previous in unit.RecoveryFamily.Where(x => x.State is AuthenticationOperationState.AwaitingRecoveryProof or AuthenticationOperationState.AwaitingNewFactor))
            {
                AdmissionOperations.Finish(previous, now, cancelled: true);
                previous.State = AuthenticationOperationState.Superseded;
            }
            if (!unit.Security.LocalMfaRecoveryRequired || unit.Security.ProtectedTotpSecret is not null)
            {
                unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
                unit.Security.FactorGeneration = checked(unit.Security.FactorGeneration + 1);
                unit.Security.ProtectedTotpSecret = null;
                unit.Security.LastAcceptedTotpStep = null;
                unit.Security.TotpProtectionVersion = 0;
                unit.Security.LocalMfaRecoveryRequired = true;
                unit.Audit("MfaReset", now, actorUserID: actor.User.ID, verificationReference: request.VerificationReference.Trim());
            }
            var credential = MfaRecoveryCredential.Create(services.Options.OperationKey);
            var root = new AuthenticationOperation
            {
                ID = Guid.NewGuid(), UserID = unit.User.ID, Purpose = AuthenticationOperationPurpose.MfaRecovery,
                State = AuthenticationOperationState.AwaitingRecoveryProof, SecurityVersion = unit.Security.SecurityVersion,
                FactorGeneration = unit.Security.FactorGeneration, PolicyRevision = unit.Policy.Revision,
                ClientID = services.Client.ID, Audience = services.Client.Audience, External = services.Client.External,
                CreatedAt = now, ExpiresAt = now.AddMinutes(15), RecoveryCodeDigest = credential.Digest,
                OutstandingRecoveryUserID = unit.User.ID
            };
            unit.Security.MfaRecoveryOperationID = root.ID;
            unit.AddOperation(root);
            unit.Audit("MfaRecoveryIssued", now, root.ID, actor.User.ID, request.VerificationReference.Trim());
            return Task.FromResult<AuthOutcome>(new MfaRecoveryCodeIssued(credential.Code, root.ExpiresAt));
        }, ct);
    });

    internal static Task<AuthOutcome> RecoverMfaAsync(IdentityAdmissionServices services, RecoverMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge)) return Refuse(AuthenticationFailure.InvalidRequest);
        var snapshot = await services.Store.ReadProofAsync(request.Username.Trim(), services.Client, ct);
        if (snapshot?.Security.MfaRecoveryOperationID is not { } rootID) return Refuse(AuthenticationFailure.InvalidProof);
        var passwordValid = HashService.VerifyVersionedPassword(request.CurrentPassword, snapshot.User.Salt, snapshot.User.PasswordHash);
        var provenAt = services.Clock.GetUtcNow();
        services.Observe?.Invoke("RecoveryProof");
        return await services.Store.AdmitAsync<AuthOutcome>(snapshot.User.ID, rootID, services.Client, unit =>
        {
            var refusal = CommonRefusal(services, unit, snapshot.Security.SecurityVersion, snapshot.Policy.Revision);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var root = unit.Operation;
            if (root is null || root.UserID != unit.User.ID || root.Purpose != AuthenticationOperationPurpose.MfaRecovery ||
                root.State != AuthenticationOperationState.AwaitingRecoveryProof || root.ParentID is not null ||
                unit.Security.MfaRecoveryOperationID != root.ID || !unit.Security.LocalMfaRecoveryRequired ||
                root.SecurityVersion != unit.Security.SecurityVersion || root.FactorGeneration != unit.Security.FactorGeneration ||
                root.PolicyRevision != unit.Policy.Revision || !SameCredential(snapshot, unit) ||
                root.ClientID != services.Client.ID || root.Audience != services.Client.Audience || root.External != services.Client.External)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            var now = services.Clock.GetUtcNow();
            if (now >= root.ExpiresAt || now < root.CreatedAt || now >= provenAt.AddMinutes(5))
            {
                AdmissionOperations.Finish(root, now, cancelled: true);
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            }
            if (BudgetExhausted(unit.Security, now) || root.FailedAttempts >= 5 || unit.User.LockDownUntil > now.UtcDateTime)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
            var codeValid = MfaRecoveryCredential.Verify(request.RecoveryCode, root.RecoveryCodeDigest, services.Options.OperationKey);
            if (!passwordValid || !codeValid)
            {
                AdmissionOperations.FailedAttempt(unit, now, "InvalidRecoveryProof");
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            AdmissionOperations.Finish(root, now);
            return Task.FromResult<AuthOutcome>(AdmissionOperations.Create(services, unit, AuthenticationOperationPurpose.MfaRecovery,
                AuthenticationOperationState.AwaitingNewFactor, request.CodeChallenge, root.CreatedAt,
                Earlier(root.ExpiresAt, now.AddMinutes(10)), passwordProvenAt: provenAt, prepareNewFactor: true,
                parentID: root.ID, failedAttempts: root.FailedAttempts));
        }, ct);
    });
}
