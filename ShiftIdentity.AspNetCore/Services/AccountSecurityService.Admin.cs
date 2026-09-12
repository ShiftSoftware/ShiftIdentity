using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

/// <summary>
/// Administrator account mutations. Each runs under the ordered actor/target admission lock, requires Users.Write
/// with operator proof younger than five minutes, increments the target's SecurityVersion once and never issues a
/// session. Operators cannot target themselves, built-in accounts or deleted accounts.
/// </summary>
internal static partial class AccountSecurityService
{
    internal static Task<AuthOutcome> SetPasswordAsync(IdentityAdmissionServices services, string? authorization,
        AdminSetPasswordRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (signedIn.Proof.UserID == request.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        // First admission checks the operator and captures the target credential; the expensive hash runs outside the lock.
        var read = await services.Store.AdmitAdminAsync(signedIn.Proof.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, signedIn);
            return Task.FromResult(new SnapshotResult(refusal is null ? new(unit.User, unit.Security, unit.Policy) : null, refusal));
        }, ct);
        if (read.Failure is not null) return read.Failure;
        var snapshot = read.Snapshot!;
        var failure = services.PasswordPolicy.Validate(request.NewPassword, snapshot.User.Username);
        if (failure is not null) return new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, failure);
        if (HashService.VerifyVersionedPassword(request.NewPassword, snapshot.User.Salt, snapshot.User.PasswordHash))
            return new AuthenticationRefused(AuthenticationFailure.InvalidNewPassword, PasswordPolicyFailure.SameAsCurrent);
        var candidate = HashService.GenerateVersionedHash(request.NewPassword);
        services.Observe?.Invoke("AdminPasswordPrepared");
        return await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, signedIn);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            // A credential that changed since the snapshot (self-service change, reset, another operator) is never overwritten.
            if (!SameCredential(snapshot, unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var now = services.Clock.GetUtcNow();
            unit.User.PasswordHash = candidate.PasswordHash;
            unit.User.Salt = candidate.Salt;
            unit.User.RequireChangePassword = request.RequireChangeAtNextLogin;
            unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
            unit.Audit(request.RequireChangeAtNextLogin ? "AdminPasswordSetRequiringChange" : "AdminPasswordSet", now, actorUserID: actor.User.ID);
            services.Observe?.Invoke("AdminAccountMutation");
            return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Password, true, unit.Security.SecurityVersion));
        }, ct);
    });

    internal static Task<AuthOutcome> ChangeUsernameAsync(IdentityAdmissionServices services, string? authorization,
        AdminUsernameChangeRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        var username = request?.Username?.Trim();
        if (!Valid(request) || string.IsNullOrEmpty(username) || username.Any(char.IsControl)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (signedIn.Proof.UserID == request!.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        return await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, request.UserID, services.Client, async (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, signedIn);
            if (refusal is not null) return refusal;
            // Uninitialized lookup keys are an operational gap, not something a request silently repairs.
            if (!RecoveryContact.LookupMatches(unit.User, unit.Security)) return Refuse(AuthenticationFailure.Unavailable);
            if (string.Equals(unit.User.Username, username, StringComparison.Ordinal))
                return new AdminAccountChanged(AdminAccountChange.Username, false, unit.Security.SecurityVersion);
            if (await services.Store.IdentifierInUseAsync(username, unit.User.ID, ct)) return Refuse(AuthenticationFailure.DuplicateIdentifier);
            var now = services.Clock.GetUtcNow();
            unit.User.Username = username;
            unit.Security.UsernameLookupKey = RecoveryContact.Key(username);
            unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
            unit.Audit("AdminUsernameChanged", now, actorUserID: actor.User.ID);
            services.Observe?.Invoke("AdminAccountMutation");
            return new AdminAccountChanged(AdminAccountChange.Username, true, unit.Security.SecurityVersion);
        }, ct);
    });

    internal static Task<AuthOutcome> ChangeEmailAsync(IdentityAdmissionServices services, string? authorization,
        AdminEmailChangeRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim();
        if (email is not null && !IsDeliveryAddress(email)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (signedIn.Proof.UserID == request.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        var outcome = await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, request.UserID, services.Client, async (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, signedIn);
            if (refusal is not null) return refusal;
            if (!RecoveryContact.LookupMatches(unit.User, unit.Security)) return Refuse(AuthenticationFailure.Unavailable);
            if (RecoveryContact.Key(unit.User.Email) == RecoveryContact.Key(email))
                return new AdminAccountChanged(AdminAccountChange.Email, false, unit.Security.SecurityVersion);
            if (email is not null && await services.Store.IdentifierInUseAsync(email, unit.User.ID, ct)) return Refuse(AuthenticationFailure.DuplicateIdentifier);
            var now = services.Clock.GetUtcNow();
            // Clears verification, advances contact/security versions, records admin-assigned recovery authority and supersedes links.
            RecoveryContact.ApplyAuthorizedEmailChange(unit, unit.Security.SecurityVersion, unit.Security.ContactRevision, email,
                RecoveryEmailProvenance.TrustedAdminAssignment, now);
            unit.Audit("AdminEmailChanged", now, actorUserID: actor.User.ID);
            services.Observe?.Invoke("AdminAccountMutation");
            return new AdminAccountChanged(AdminAccountChange.Email, true, unit.Security.SecurityVersion);
        }, ct);
        if (outcome is not AdminAccountChanged { Applied: true } changed || email is null || !request.SendVerification) return outcome;
        // The contact change has committed. Verification is a separate grant with its own budget, audit and awaited handoff.
        return changed with { Delivery = await AdminSecurityLinkAsync(services, authorization, request.UserID, AuthenticationOperationPurpose.EmailVerify, ct) };
    });

    internal static Task<AuthOutcome> SetActiveAsync(IdentityAdmissionServices services, string? authorization,
        AdminAccountStatusRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (signedIn.Proof.UserID == request.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        return await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, signedIn, allowInactive: true);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (unit.User.IsActive == request.Active)
                return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Active, false, unit.Security.SecurityVersion));
            var now = services.Clock.GetUtcNow();
            // Re-enabling bumps as well: credentials issued before deactivation never come back.
            unit.User.IsActive = request.Active;
            unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);
            unit.Audit(request.Active ? "AccountActivated" : "AccountDeactivated", now, actorUserID: actor.User.ID);
            services.Observe?.Invoke("AdminAccountMutation");
            return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Active, true, unit.Security.SecurityVersion));
        }, ct);
    });

    /// <summary>
    /// Operator checks shared by every administrator route: current-version session, proof younger than five
    /// minutes, no pending local step for the operator, and the named permission from the operator's own trees.
    /// </summary>
    internal static AuthenticationRefused? ActorRefusal(IdentityAdmissionServices services, IdentitySecurityTransaction actor,
        SignedInContext signedIn, Func<TypeAuthContext, bool> permitted)
    {
        var refusal = SignedInRefusal(services, actor, signedIn);
        if (refusal is not null) return refusal;
        var now = services.Clock.GetUtcNow();
        if (now < signedIn.Proof.AuthenticatedAt || now >= signedIn.Proof.AuthenticatedAt.AddMinutes(5) || LocalStep(actor, signedIn.Proof.MfaSatisfied) is not null)
            return Refuse(AuthenticationFailure.InvalidProof);
        var trees = actor.User.AccessTrees.Select(x => x.AccessTree.Tree).ToList();
        if (!string.IsNullOrWhiteSpace(actor.User.AccessTree)) trees.Add(actor.User.AccessTree);
        return permitted(new TypeAuthContext(trees, typeof(ShiftIdentityActions))) ? null : Refuse(AuthenticationFailure.ClientDenied);
    }

    private static AuthenticationRefused? AdminTargetRefusal(IdentityAdmissionServices services, IdentitySecurityTransaction actor,
        IdentitySecurityTransaction unit, SignedInContext signedIn, bool allowInactive = false)
    {
        var refusal = ActorRefusal(services, actor, signedIn, permissions => permissions.CanWrite(ShiftIdentityActions.Users));
        if (refusal is not null) return refusal;
        if (unit.User.IsDeleted || (!allowInactive && !unit.User.IsActive)) return Refuse(AuthenticationFailure.AccountUnavailable);
        return unit.User.IsProtected ? Refuse(AuthenticationFailure.ClientDenied) : null;
    }
}
