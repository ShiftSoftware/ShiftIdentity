using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using OtpNet;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

public partial class AuthService
{
    // Staged entry points share the existing coordinator. Only isolated fixtures map them in this slice.
    // Production registration remains unchanged until all issuers and security writers are adapted.
    internal static Task<AuthOutcome> BeginLoginAsync(IdentityAdmissionServices services, PasswordLoginRequest request, CancellationToken ct) =>
        AtBoundary(async () =>
        {
            var startedAt = services.Clock.GetUtcNow();
            if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge))
                return Refuse(AuthenticationFailure.InvalidRequest);
            var snapshot = await services.Store.ReadProofAsync(request.Username.Trim(), services.Client, ct);
            if (snapshot is null) return Refuse(AuthenticationFailure.InvalidProof);
            var validPassword = HashService.VerifyVersionedPassword(request.Password, snapshot.User.Salt, snapshot.User.PasswordHash);
            var provenAt = services.Clock.GetUtcNow();
            var upgrade = validPassword && VersionedPasswordHash.NeedsUpgrade(snapshot.User.PasswordHash)
                ? HashService.GenerateVersionedHash(request.Password) : null;
            services.Observe?.Invoke("PasswordProof");
            return await services.Store.AdmitAsync<AuthOutcome>(snapshot.User.ID, null, services.Client, unit =>
            {
                var now = services.Clock.GetUtcNow();
                var refusal = CommonRefusal(services, unit, snapshot.Security.SecurityVersion, snapshot.Policy.Revision);
                if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
                if (!CryptographicOperations.FixedTimeEquals(snapshot.User.PasswordHash, unit.User.PasswordHash) ||
                    !CryptographicOperations.FixedTimeEquals(snapshot.User.Salt, unit.User.Salt) ||
                    snapshot.Security.FactorGeneration != unit.Security.FactorGeneration)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
                if (BudgetExhausted(unit.Security, now))
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
                if (unit.User.LockDownUntil is { } lockedUntil && lockedUntil > now.UtcDateTime)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
                if (!validPassword)
                {
                    FailedProof(unit.Security, now);
                    unit.Audit("InvalidPassword", now);
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
                }
                if (now >= startedAt.AddMinutes(5)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
                if (upgrade is not null)
                {
                    unit.User.PasswordHash = upgrade.PasswordHash;
                    unit.User.Salt = upgrade.Salt;
                }
                if (unit.User.RequireChangePassword)
                    return Task.FromResult<AuthOutcome>(AdmissionOperations.Create(services, unit,
                        AuthenticationOperationPurpose.PasswordChange, AuthenticationOperationState.AwaitingNewPassword,
                        request.CodeChallenge, startedAt, startedAt.AddMinutes(5), PasswordChangeOrigin.RequiredLogin, provenAt));
                var next = LocalStep(unit, mfaSatisfied: false);
                if (next == AuthenticationStep.NewMfa)
                    return Task.FromResult<AuthOutcome>(AdmissionOperations.Create(services, unit,
                        AuthenticationOperationPurpose.MfaEnrollment, AuthenticationOperationState.AwaitingNewFactor,
                        request.CodeChallenge, startedAt, startedAt.AddMinutes(10), passwordProvenAt: provenAt, prepareNewFactor: true));
                if (next is not null && next != AuthenticationStep.ExistingMfa)
                    return Task.FromResult<AuthOutcome>(Restricted(next.Value, now));
                if (next == AuthenticationStep.ExistingMfa)
                {
                    return Task.FromResult<AuthOutcome>(AdmissionOperations.Create(services, unit,
                        AuthenticationOperationPurpose.Login, AuthenticationOperationState.AwaitingMfa,
                        request.CodeChallenge, startedAt, startedAt.AddMinutes(5), passwordProvenAt: provenAt));
                }
                var proof = Proof(unit, services, false, provenAt);
                return Task.FromResult(Issue(services, unit, proof, now));
            }, ct);
        });

    internal static Task<AuthOutcome> CompleteLoginMfaAsync(IdentityAdmissionServices services, string? handle,
        CompleteMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var reference = await AdmissionOperations.ReadAsync(services, handle, AuthenticationOperationPurpose.Login, ct);
        if (reference is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(reference.UserID, reference.ID, services.Client, unit =>
        {
            var refusal = AdmissionOperations.Check(services, unit, reference, request.CodeVerifier,
                AuthenticationOperationPurpose.Login, AuthenticationOperationState.AwaitingMfa);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (LocalStep(unit, false) != AuthenticationStep.ExistingMfa)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            refusal = AdmissionOperations.VerifyMfa(services, unit, request.Code);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var op = unit.Operation!;
            var now = services.Clock.GetUtcNow();
            AdmissionOperations.Finish(op, now);
            var remaining = LocalStep(unit, true);
            return Task.FromResult<AuthOutcome>(remaining is not null ? Restricted(remaining.Value, now)
                : Issue(services, unit, Proof(unit, services, true, op.PasswordProvenAt ?? op.CreatedAt), now));
        }, ct);
    });
    internal static Task<AuthOutcome> RenewSessionAsync(IdentityAdmissionServices services, RenewSessionRequest request, CancellationToken ct,
        bool updateLastSeen = false) =>
        AtBoundary(async () =>
        {
            if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
            var proof = services.Tokens.ValidateRefresh(request.RefreshToken, services.Client);
            if (proof is null) return Refuse(AuthenticationFailure.InvalidGrant);
            services.Observe?.Invoke("RefreshProof");
            return await services.Store.AdmitAsync<AuthOutcome>(proof.UserID, null, services.Client, unit =>
            {
                var refusal = CommonRefusal(services, unit, proof.SecurityVersion, proof.PolicyRevision);
                if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
                // Time can pass while waiting for the admission lock. Revalidate the same credential.
                if (services.Tokens.ValidateRefresh(request.RefreshToken, services.Client) is null)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
                if (unit.Security.FactorGeneration != proof.FactorGeneration)
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
                if (!AppSessionIsCurrent(unit, proof))
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
                if (proof.Subject != services.HashIds.Encode<UserDTO>(unit.User.ID))
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
                var now = services.Clock.GetUtcNow();
                var next = LocalStep(unit, proof.MfaSatisfied);
                if (next is not null) return Task.FromResult<AuthOutcome>(Restricted(next.Value, now));
                if (updateLastSeen)
                {
                    // The deployed refresh timer also maintains LastSeen through this route.
                    unit.User.UserLog ??= new Data.Entities.UserLog();
                    unit.User.UserLog.LastSeen = now;
                }
                return Task.FromResult(Issue(services, unit, proof, now, false));
            }, ct);
        });

}
