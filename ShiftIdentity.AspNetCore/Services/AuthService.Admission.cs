using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
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
    // Production LoginAsync/RefreshAsync and DI remain unchanged until all security writers are adapted.
    internal static Task<AuthOutcome> BeginLoginAsync(IdentityAdmissionServices services, PasswordLoginRequest request, CancellationToken ct) =>
        AtBoundary(async () =>
        {
            if (!Valid(request) || !OperationCredential.IsChallenge(request.CodeChallenge))
                return Refuse(AuthenticationFailure.InvalidRequest);
            var snapshot = await services.Store.ReadProofAsync(request.Username.Trim(), services.Client, ct);
            if (snapshot is null) return Refuse(AuthenticationFailure.InvalidProof);
            var validPassword = HashService.VerifyPassword(request.Password, snapshot.User.Salt, snapshot.User.PasswordHash);
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
                var next = LocalStep(unit, mfaSatisfied: false);
                if (next is not null && next != AuthenticationStep.ExistingMfa)
                    return Task.FromResult<AuthOutcome>(Restricted(next.Value, now));
                if (next == AuthenticationStep.ExistingMfa)
                {
                    var credential = OperationCredential.Create(services.Options.OperationKey);
                    var operation = new AuthenticationOperation
                    {
                        ID = credential.ID, UserID = unit.User.ID,
                        Purpose = AuthenticationOperationPurpose.Login, State = AuthenticationOperationState.AwaitingMfa,
                        SecurityVersion = unit.Security.SecurityVersion, FactorGeneration = unit.Security.FactorGeneration,
                        PolicyRevision = unit.Policy.Revision, ClientID = services.Client.ID,
                        Audience = services.Client.Audience, External = services.Client.External,
                        HandleDigest = credential.Digest, CodeChallenge = request.CodeChallenge,
                        CreatedAt = now, ExpiresAt = now.AddMinutes(5)
                    };
                    unit.AddOperation(operation);
                    unit.Audit("LoginChallenge", now, operation.ID);
                    return Task.FromResult<AuthOutcome>(new ChallengeRequired(new(AuthenticationStep.ExistingMfa,
                        credential.Handle, operation.ExpiresAt)));
                }
                var proof = Proof(unit, services, false, now);
                return Task.FromResult(Issue(services, unit, proof, now));
            }, ct);
        });

    internal static Task<AuthOutcome> CompleteLoginMfaAsync(IdentityAdmissionServices services, string? handle,
        CompleteMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        if (!OperationCredential.TryRead(handle, services.Options.OperationKey, out var id, out var digest))
            return Refuse(AuthenticationFailure.InvalidGrant);
        var found = await services.Store.ReadOperationAsync(id, ct);
        if (found is null || !CryptographicOperations.FixedTimeEquals(found.HandleDigest, digest))
            return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(found.UserID, id, services.Client, unit =>
        {
            var now = services.Clock.GetUtcNow();
            var op = unit.Operation;
            if (op is null || op.UserID != unit.User.ID || op.Purpose != AuthenticationOperationPurpose.Login ||
                !CryptographicOperations.FixedTimeEquals(op.HandleDigest, digest) ||
                op.ClientID != services.Client.ID || op.Audience != services.Client.Audience || op.External != services.Client.External ||
                !OperationCredential.VerifyChallenge(op.CodeChallenge, request.CodeVerifier))
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
            if (op.State != AuthenticationOperationState.AwaitingMfa)
                return Task.FromResult<AuthOutcome>(Refuse(op.State == AuthenticationOperationState.Locked
                    ? AuthenticationFailure.AttemptsExhausted : AuthenticationFailure.InvalidGrant));
            if (now >= op.ExpiresAt || now < op.CreatedAt)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.Expired));
            var refusal = CommonRefusal(services, unit, op.SecurityVersion, op.PolicyRevision);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (unit.Security.FactorGeneration != op.FactorGeneration)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (BudgetExhausted(unit.Security, now) || op.FailedAttempts >= 5)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
            var next = LocalStep(unit, false);
            if (next != AuthenticationStep.ExistingMfa || unit.Security.ProtectedTotpSecret is null)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var secret = services.FactorProtector.Unprotect(unit.Security.ProtectedTotpSecret);
            long matchedStep;
            bool valid;
            try
            {
                var totp = new Totp(secret, unit.Policy.TotpPeriodSeconds, totpSize: unit.Policy.TotpDigits);
                valid = totp.VerifyTotp(now.UtcDateTime, request.Code, out matchedStep,
                    new VerificationWindow(unit.Policy.TotpWindowPast, unit.Policy.TotpWindowFuture));
            }
            finally { CryptographicOperations.ZeroMemory(secret); }
            if (!valid || matchedStep <= (unit.Security.LastAcceptedTotpStep ?? -1))
            {
                op.FailedAttempts++;
                if (op.FailedAttempts == 5) op.State = AuthenticationOperationState.Locked;
                FailedProof(unit.Security, now);
                unit.Audit("InvalidMfa", now, op.ID);
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            op.State = AuthenticationOperationState.Completed;
            op.CompletedAt = now;
            op.HandleDigest = [];
            unit.Security.LastAcceptedTotpStep = matchedStep;
            unit.Audit("MfaCompleted", now, op.ID);
            var remaining = LocalStep(unit, true);
            return Task.FromResult<AuthOutcome>(remaining is not null ? Restricted(remaining.Value, now)
                : Issue(services, unit, Proof(unit, services, true, op.CreatedAt), now));
        }, ct);
    });

    internal static Task<AuthOutcome> RenewSessionAsync(IdentityAdmissionServices services, RenewSessionRequest request, CancellationToken ct) =>
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
                if (proof.Subject != services.HashIds.Encode<UserDTO>(unit.User.ID))
                    return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidGrant));
                var now = services.Clock.GetUtcNow();
                var next = LocalStep(unit, proof.MfaSatisfied);
                return Task.FromResult<AuthOutcome>(next is not null ? Restricted(next.Value, now) : Issue(services, unit, proof, now, false));
            }, ct);
        });

    private static AuthenticationRefused? CommonRefusal(IdentityAdmissionServices services,
        IdentitySecurityTransaction unit, long version, long policy)
    {
        services.Observe?.Invoke("AdmissionLock");
        if (!unit.User.IsActive || unit.User.IsDeleted) return Refuse(AuthenticationFailure.AccountUnavailable);
        if (unit.Security.SecurityVersion != version || unit.Policy.Revision != policy)
            return Refuse(AuthenticationFailure.StaleOperation);
        if (services.Options.PolicyRevision != unit.Policy.Revision) return Refuse(AuthenticationFailure.Unavailable);
        return null;
    }

    private static AuthenticationStep? LocalStep(IdentitySecurityTransaction unit, bool mfaSatisfied)
    {
        if (unit.User.RequireChangePassword) return AuthenticationStep.PasswordChange;
        if (unit.Security.LocalMfaRecoveryRequired) return AuthenticationStep.MfaRecovery;
        if (unit.Policy.MfaEnabled)
        {
            if (unit.Security.ProtectedTotpSecret is null && unit.Policy.MfaMandatory) return AuthenticationStep.NewMfa;
            if (unit.Security.ProtectedTotpSecret is not null && !mfaSatisfied) return AuthenticationStep.ExistingMfa;
        }
        if (unit.Policy.RequireVerifiedEmail && !string.IsNullOrWhiteSpace(unit.User.Email) && !unit.User.EmailVerified)
            return AuthenticationStep.EmailVerification;
        return null;
    }

    private static SessionProof Proof(IdentitySecurityTransaction unit, IdentityAdmissionServices services, bool mfa, DateTimeOffset now) =>
        new(unit.User.ID, unit.Security.SecurityVersion, unit.Policy.Revision, unit.Security.FactorGeneration,
            mfa, now, services.Client.ID, services.Client.Audience, services.Client.External, services.HashIds.Encode<UserDTO>(unit.User.ID));

    private static AuthOutcome Issue(IdentityAdmissionServices services, IdentitySecurityTransaction unit, SessionProof proof, DateTimeOffset now, bool freshAuthentication = true)
    {
        var user = unit.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, services.HashIds.Encode<UserDTO>(user.ID)),
            new(ClaimTypes.Name, user.Username), new(ClaimTypes.GivenName, user.FullName),
            new(ShiftIdentityClaims.ExternalToken, proof.External ? "true" : "false")
        };
        if (user.CompanyID is { } company) claims.Add(new(ShiftEntity.Core.Constants.CompanyIdClaim, services.HashIds.Encode<CompanyDTO>(company)));
        if (user.RegionID is { } region) claims.Add(new(ShiftEntity.Core.Constants.RegionIdClaim, services.HashIds.Encode<RegionDTO>(region)));
        if (user.CompanyBranchID is { } branch) claims.Add(new(ShiftEntity.Core.Constants.CompanyBranchIdClaim, services.HashIds.Encode<CompanyBranchDTO>(branch)));
        if (user.CountryID is { } country) claims.Add(new(ShiftEntity.Core.Constants.CountryIdClaim, services.HashIds.Encode<CountryDTO>(country)));
        if (user.CompanyBranch?.CityID is { } city) claims.Add(new(ShiftEntity.Core.Constants.CityIdClaim, services.HashIds.Encode<CityDTO>(city)));
        claims.Add(new(ShiftEntity.Core.Constants.CompanyTypeClaim, user.Company?.CompanyType.ToString() ?? ""));
        foreach (var team in user.TeamUsers) claims.Add(new(ShiftEntity.Core.Constants.TeamIdsClaim, services.HashIds.Encode<TeamDTO>(team.TeamID)));
        if (user.Email is not null) claims.Add(new(ClaimTypes.Email, user.Email));
        if (user.Phone is not null) claims.Add(new(ClaimTypes.MobilePhone, user.Phone));
        if (!string.IsNullOrWhiteSpace(user.AccessTree)) claims.Add(new(TypeAuthClaimTypes.AccessTree, user.AccessTree));
        foreach (var tree in user.AccessTrees) claims.Add(new(TypeAuthClaimTypes.AccessTree, tree.AccessTree.Tree));
        if (freshAuthentication)
        {
            unit.Security.FailedProofs = 0;
            unit.Security.FailureWindowStart = null;
            unit.Audit("SessionIssued", now);
        }
        return new SessionIssued(services.Tokens.Issue(new(proof, user.Username, user.FullName, claims.AsReadOnly(), now)));
    }

    private static bool BudgetExhausted(UserSecurityState security, DateTimeOffset now) =>
        security.FailedProofs >= 10 && security.FailureWindowStart is { } start && now < start.AddMinutes(15);

    private static void FailedProof(UserSecurityState security, DateTimeOffset now)
    {
        if (security.FailureWindowStart is not { } start || now >= start.AddMinutes(15))
        {
            security.FailureWindowStart = now;
            security.FailedProofs = 0;
        }
        security.FailedProofs++;
    }

    private static bool Valid(object? value) => value is not null &&
        Validator.TryValidateObject(value, new ValidationContext(value), new List<ValidationResult>(), true);

    private static AuthenticationRefused Refuse(AuthenticationFailure failure) => new(failure);
    private static ChallengeRequired Restricted(AuthenticationStep step, DateTimeOffset now) => new(new(step, null, now.AddMinutes(5)));

    private static async Task<AuthOutcome> AtBoundary(Func<Task<AuthOutcome>> action)
    {
        try { return await action(); }
        catch (IdentitySecurityConflictException) { return Refuse(AuthenticationFailure.StaleOperation); }
        catch (IdentitySecurityUnavailableException) { return Refuse(AuthenticationFailure.Unavailable); }
        catch (CryptographicException) { return Refuse(AuthenticationFailure.Unavailable); }
    }
}
