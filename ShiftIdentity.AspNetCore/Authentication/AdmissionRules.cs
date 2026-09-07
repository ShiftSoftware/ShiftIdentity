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

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;

internal static class AdmissionRules
{
    internal static AuthenticationRefused? CommonRefusal(IdentityAdmissionServices services,
        IdentitySecurityTransaction unit, long version, long policy)
    {
        services.Observe?.Invoke("AdmissionLock");
        if (!unit.User.IsActive || unit.User.IsDeleted) return Refuse(AuthenticationFailure.AccountUnavailable);
        if (unit.Security.SecurityVersion != version || unit.Policy.Revision != policy)
            return Refuse(AuthenticationFailure.StaleOperation);
        if (services.Options.PolicyRevision != unit.Policy.Revision) return Refuse(AuthenticationFailure.Unavailable);
        return null;
    }

    internal static AuthenticationStep? LocalStep(IdentitySecurityTransaction unit, bool mfaSatisfied, bool ignorePasswordChange = false)
    {
        if (unit.User.RequireChangePassword && !ignorePasswordChange) return AuthenticationStep.PasswordChange;
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

    internal static SessionProof Proof(IdentitySecurityTransaction unit, IdentityAdmissionServices services, bool mfa, DateTimeOffset now) =>
        new(unit.User.ID, unit.Security.SecurityVersion, unit.Policy.Revision, unit.Security.FactorGeneration,
            mfa, now, services.Client.ID, services.Client.Audience, services.Client.External, services.HashIds.Encode<UserDTO>(unit.User.ID));

    internal static AuthOutcome Issue(IdentityAdmissionServices services, IdentitySecurityTransaction unit, SessionProof proof, DateTimeOffset now, bool freshAuthentication = true)
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

    internal static bool BudgetExhausted(UserSecurityState security, DateTimeOffset now) =>
        security.FailedProofs >= 10 && security.FailureWindowStart is { } start && now < start.AddMinutes(15);

    internal static void FailedProof(UserSecurityState security, DateTimeOffset now)
    {
        if (security.FailureWindowStart is not { } start || now >= start.AddMinutes(15))
        {
            security.FailureWindowStart = now;
            security.FailedProofs = 0;
        }
        security.FailedProofs++;
    }

    internal static bool Valid(object? value) => value is not null &&
        Validator.TryValidateObject(value, new ValidationContext(value), new List<ValidationResult>(), true);

    internal static AuthenticationRefused Refuse(AuthenticationFailure failure) => new(failure);
    internal static ChallengeRequired Restricted(AuthenticationStep step, DateTimeOffset now) => new(new(step, null, now.AddMinutes(5)));

    internal static async Task<AuthOutcome> AtBoundary(Func<Task<AuthOutcome>> action)
    {
        try { return await action(); }
        catch (IdentitySecurityConflictException) { return Refuse(AuthenticationFailure.StaleOperation); }
        catch (IdentitySecurityUnavailableException) { return Refuse(AuthenticationFailure.Unavailable); }
        catch (CryptographicException) { return Refuse(AuthenticationFailure.Unavailable); }
        catch (OperationCanceledException) { return Refuse(AuthenticationFailure.Unavailable); }
    }
}
