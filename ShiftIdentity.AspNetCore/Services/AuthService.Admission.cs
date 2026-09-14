using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
                return Task.FromResult(ContinuePasswordLogin(services, unit, request.CodeChallenge, startedAt, provenAt));
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
                var next = ExistingSessionStep(unit, proof, now);
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

    internal static Task<AuthOutcome> ExchangeLegacyRefreshAsync(IdentityAdmissionServices services,
        RenewSessionRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || services.Client.External || services.LegacyRefreshTokens is not { } legacyTokens)
            return Refuse(AuthenticationFailure.InvalidGrant);
        var legacy = legacyTokens.Validate(request.RefreshToken);
        if (legacy is null) return Refuse(AuthenticationFailure.InvalidGrant);
        long userID;
        try
        {
            userID = services.HashIds.Decode<UserDTO>(legacy.Subject);
            if (userID <= 0 && !long.TryParse(legacy.Subject, NumberStyles.None, CultureInfo.InvariantCulture, out userID))
                return Refuse(AuthenticationFailure.InvalidGrant);
        }
        catch (Exception e) when (e is ArgumentException or FormatException or OverflowException)
        {
            return Refuse(AuthenticationFailure.InvalidGrant);
        }
        if (userID <= 0) return Refuse(AuthenticationFailure.InvalidGrant);
        var digest = HMACSHA256.HashData(services.Options.OperationKey, Encoding.UTF8.GetBytes(request.RefreshToken));
        services.Observe?.Invoke("LegacyRefreshProof");
        return await services.Store.AdmitLegacyRefreshAsync<AuthOutcome>(userID, digest, services.Client, unit =>
        {
            if (unit.Operation is not null) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            var current = legacyTokens.Validate(request.RefreshToken);
            var now = services.Clock.GetUtcNow();
            if (current is null || current.Subject != legacy.Subject || current.ExpiresAt != legacy.ExpiresAt || now >= legacy.ExpiresAt)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            var refusal = LegacyRefreshRefusal(services, unit);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var proof = new SessionProof(unit.User.ID, unit.Security.SecurityVersion, unit.Policy.Revision,
                unit.Security.FactorGeneration, false, DateTimeOffset.UnixEpoch, services.Client.ID,
                services.Client.Audience, false, services.HashIds.Encode<UserDTO>(unit.User.ID),
                LegacyCompatibilityExpiresAt: legacy.ExpiresAt);
            var exchange = new AuthenticationOperation
            {
                ID = Guid.NewGuid(), UserID = unit.User.ID,
                Purpose = AuthenticationOperationPurpose.LegacyRefreshExchange,
                State = AuthenticationOperationState.Completed,
                SecurityVersion = unit.Security.SecurityVersion, PolicyRevision = unit.Policy.Revision,
                FactorGeneration = unit.Security.FactorGeneration, ClientID = services.Client.ID,
                Audience = services.Client.Audience, External = false, HandleDigest = digest,
                CreatedAt = now, ExpiresAt = legacy.ExpiresAt, CompletedAt = now
            };
            unit.AddOperation(exchange);
            unit.User.UserLog ??= new Data.Entities.UserLog();
            unit.User.UserLog.LastSeen = now;
            unit.Audit("LegacyRefreshExchanged", now, exchange.ID);
            return Task.FromResult(Issue(services, unit, proof, now, freshAuthentication: false));
        }, ct);
    });

}
