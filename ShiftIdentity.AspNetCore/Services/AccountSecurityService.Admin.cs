using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

/// <summary>
/// The operator behind an administrator mutation. A staged v2 session carries its proof (version, time, factor);
/// a host-authenticated legacy session carries only the user ID, so its checks are limited to current state and
/// current permission.
/// </summary>
internal sealed record AdminActor(long UserID, SignedInContext? Session);

/// <summary>
/// Administrator account mutations. Each runs under the ordered actor/target admission lock, requires Users.Write
/// with operator proof younger than five minutes, increments the target's SecurityVersion once and never issues a
/// session. Operators cannot target themselves, built-in accounts or deleted accounts. An inactive target accepts
/// every change: an inactive account holds no session, and correcting it must not require activating it first.
/// The mutation bodies are shared with the legacy administrator writers, so each security-relevant change has one
/// implementation.
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
        var who = new AdminActor(signedIn.Proof.UserID, signedIn);
        // First admission checks the operator and captures the target credential; the expensive hash runs outside the lock.
        var read = await services.Store.AdmitAdminAsync(who.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, who);
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
        return await services.Store.AdmitAdminAsync<AuthOutcome>(who.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, who);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            // A credential that changed since the snapshot (self-service change, reset, another operator) is never overwritten.
            if (!SameCredential(snapshot, unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            var now = services.Clock.GetUtcNow();
            ApplyPassword(unit, candidate, request.RequireChangeAtNextLogin);
            Bump(unit);
            unit.Audit(PasswordAudit(request.RequireChangeAtNextLogin), now, actorUserID: actor.User.ID);
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
        var who = new AdminActor(signedIn.Proof.UserID, signedIn);
        return await services.Store.AdmitAdminAsync<AuthOutcome>(who.UserID, request.UserID, services.Client, async (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, who);
            if (refusal is not null) return refusal;
            var (failure, applied) = await ApplyUsernameAsync(services, unit, username, ct);
            if (failure is not null) return failure;
            if (!applied) return new AdminAccountChanged(AdminAccountChange.Username, false, unit.Security.SecurityVersion);
            var now = services.Clock.GetUtcNow();
            Bump(unit);
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
        var who = new AdminActor(signedIn.Proof.UserID, signedIn);
        var outcome = await services.Store.AdmitAdminAsync<AuthOutcome>(who.UserID, request.UserID, services.Client, async (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, who);
            if (refusal is not null) return refusal;
            var now = services.Clock.GetUtcNow();
            var (failure, applied) = await ApplyEmailAsync(services, unit, email, now, ct);
            if (failure is not null) return failure;
            if (!applied) return new AdminAccountChanged(AdminAccountChange.Email, false, unit.Security.SecurityVersion);
            unit.Audit("AdminEmailChanged", now, actorUserID: actor.User.ID);
            services.Observe?.Invoke("AdminAccountMutation");
            return new AdminAccountChanged(AdminAccountChange.Email, true, unit.Security.SecurityVersion);
        }, ct);
        if (outcome is not AdminAccountChanged { Applied: true } changed || email is null || !request.SendVerification) return outcome;
        // The contact change has committed. Verification is a separate grant with its own budget, audit and awaited handoff.
        return changed with { Delivery = await AdminSecurityLinkAsync(services, who, request.UserID, AuthenticationOperationPurpose.EmailVerify, ct) };
    });

    internal static Task<AuthOutcome> SetActiveAsync(IdentityAdmissionServices services, string? authorization,
        AdminAccountStatusRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        if (signedIn.Proof.UserID == request.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        var who = new AdminActor(signedIn.Proof.UserID, signedIn);
        return await services.Store.AdmitAdminAsync<AuthOutcome>(who.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = AdminTargetRefusal(services, actor, unit, who);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            if (!ApplyStatus(unit, request.Active))
                return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Active, false, unit.Security.SecurityVersion));
            var now = services.Clock.GetUtcNow();
            Bump(unit);
            unit.Audit(request.Active ? "AccountActivated" : "AccountDeactivated", now, actorUserID: actor.User.ID);
            services.Observe?.Invoke("AdminAccountMutation");
            return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Active, true, unit.Security.SecurityVersion));
        }, ct);
    });

    // ── Shared mutation bodies ───────────────────────────────────────────────────────────────────────────────────
    // Each writes one kind of security-relevant change to the locked unit and nothing else. The caller decides the
    // version increment and the audit row, so a save that combines several changes increments the version once.

    internal static void ApplyPassword(IdentitySecurityTransaction unit, HashModel candidate, bool requireChangeAtNextLogin)
    {
        unit.User.PasswordHash = candidate.PasswordHash;
        unit.User.Salt = candidate.Salt;
        unit.User.RequireChangePassword = requireChangeAtNextLogin;
    }

    internal static string PasswordAudit(bool requireChangeAtNextLogin) =>
        requireChangeAtNextLogin ? "AdminPasswordSetRequiringChange" : "AdminPasswordSet";

    /// <summary>
    /// Disables the authenticator and requires individual local recovery, whatever the global MFA policy says: the
    /// factor generation advances, the protected secret and its replay state are cleared, and a password alone can
    /// no longer open an ordinary session. The caller decides whether the account qualifies, increments the version
    /// once and writes the audit row. The retained plaintext column is not touched; recovery keeps the startup copy
    /// from restoring the removed factor.
    /// </summary>
    internal static void DisableActiveFactor(UserSecurityState security)
    {
        security.FactorGeneration = checked(security.FactorGeneration + 1);
        security.ProtectedTotpSecret = null;
        security.LastAcceptedTotpStep = null;
        security.TotpProtectionVersion = 0;
        security.LocalMfaRecoveryRequired = true;
    }

    /// <summary>Ends the outstanding recovery root and its children; a later code or reset starts a new family.</summary>
    internal static void SupersedeRecovery(IdentitySecurityTransaction unit, DateTimeOffset now)
    {
        foreach (var previous in unit.RecoveryFamily.Where(x => x.State is AuthenticationOperationState.AwaitingRecoveryProof or AuthenticationOperationState.AwaitingNewFactor))
        {
            AdmissionOperations.Finish(previous, now, cancelled: true);
            previous.State = AuthenticationOperationState.Superseded;
        }
    }

    /// <summary>
    /// The bulk administrator reset (the deployed <c>ResetTotp</c> route): disables an active authenticator and
    /// requires individual recovery, without issuing a recovery code. That code, and the independent identity check
    /// behind it, stay with the dedicated recovery permission. An account with no active factor is left as it is: a
    /// reset without a code would only lock it out. Returns whether anything changed.
    /// </summary>
    internal static bool ApplyAuthenticatorReset(IdentitySecurityTransaction unit, DateTimeOffset now)
    {
        if (unit.Security.ProtectedTotpSecret is null) return false;
        SupersedeRecovery(unit, now);
        unit.Security.MfaRecoveryOperationID = null;
        DisableActiveFactor(unit.Security);
        return true;
    }

    /// <summary>Renames the account after a range-locked duplicate check. Returns (refusal, applied).</summary>
    internal static async Task<(AuthenticationRefused? Refusal, bool Applied)> ApplyUsernameAsync(IdentityAdmissionServices services,
        IdentitySecurityTransaction unit, string username, CancellationToken ct)
    {
        // Uninitialized lookup keys are an operational gap, not something a request silently repairs.
        if (!RecoveryContact.LookupMatches(unit.User, unit.Security)) return (Refuse(AuthenticationFailure.Unavailable), false);
        if (string.Equals(unit.User.Username, username, StringComparison.Ordinal)) return (null, false);
        if (await services.Store.IdentifierInUseAsync(username, unit.User.ID, ct)) return (Refuse(AuthenticationFailure.DuplicateIdentifier), false);
        unit.User.Username = username;
        unit.Security.UsernameLookupKey = RecoveryContact.Key(username);
        return (null, true);
    }

    /// <summary>
    /// Assigns or removes the saved email with administrator authority: verification is cleared, the contact and
    /// security versions advance, outstanding links are superseded and the admin-assigned recovery provenance is
    /// recorded. The version increment happens inside this change. Returns (refusal, applied).
    /// </summary>
    internal static async Task<(AuthenticationRefused? Refusal, bool Applied)> ApplyEmailAsync(IdentityAdmissionServices services,
        IdentitySecurityTransaction unit, string? email, DateTimeOffset now, CancellationToken ct)
    {
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (email is not null && !IsDeliveryAddress(email)) return (Refuse(AuthenticationFailure.InvalidRequest), false);
        if (!RecoveryContact.LookupMatches(unit.User, unit.Security)) return (Refuse(AuthenticationFailure.Unavailable), false);
        if (RecoveryContact.Key(unit.User.Email) == RecoveryContact.Key(email)) return (null, false);
        if (email is not null && await services.Store.IdentifierInUseAsync(email, unit.User.ID, ct)) return (Refuse(AuthenticationFailure.DuplicateIdentifier), false);
        RecoveryContact.ApplyAuthorizedEmailChange(unit, unit.Security.SecurityVersion, unit.Security.ContactRevision, email,
            RecoveryEmailProvenance.TrustedAdminAssignment, now);
        return (null, true);
    }

    /// <summary>Changes the active status. Re-enabling counts as a change too: credentials issued before deactivation never come back.</summary>
    internal static bool ApplyStatus(IdentitySecurityTransaction unit, bool active)
    {
        if (unit.User.IsActive == active) return false;
        unit.User.IsActive = active;
        return true;
    }

    /// <summary>Replaces the saved phone (already formatted by the caller) and clears its verification.</summary>
    internal static bool ApplyPhone(IdentitySecurityTransaction unit, string? phone)
    {
        if (string.Equals(unit.User.Phone, phone, StringComparison.Ordinal)) return false;
        unit.User.Phone = phone;
        unit.User.PhoneVerified = false;
        return true;
    }

    internal static void Bump(IdentitySecurityTransaction unit) =>
        unit.Security.SecurityVersion = checked(unit.Security.SecurityVersion + 1);

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
        return Permitted(actor, permitted) ? null : Refuse(AuthenticationFailure.ClientDenied);
    }

    /// <summary>
    /// The same operator checks for either kind of operator. A legacy session has no version or proof time, so only
    /// current account state, the current policy revision and the current permission can be checked for it.
    /// </summary>
    internal static AuthenticationRefused? ActorRefusal(IdentityAdmissionServices services, IdentitySecurityTransaction actor,
        AdminActor who, Func<TypeAuthContext, bool> permitted)
    {
        if (who.Session is { } signedIn) return ActorRefusal(services, actor, signedIn, permitted);
        if (actor.User.ID != who.UserID) return Refuse(AuthenticationFailure.InvalidGrant);
        if (!actor.User.IsActive || actor.User.IsDeleted) return Refuse(AuthenticationFailure.AccountUnavailable);
        if (services.Options.PolicyRevision != actor.Policy.Revision) return Refuse(AuthenticationFailure.Unavailable);
        return Permitted(actor, permitted) ? null : Refuse(AuthenticationFailure.ClientDenied);
    }

    private static bool Permitted(IdentitySecurityTransaction actor, Func<TypeAuthContext, bool> permitted)
    {
        var trees = actor.User.AccessTrees.Select(x => x.AccessTree.Tree).ToList();
        if (!string.IsNullOrWhiteSpace(actor.User.AccessTree)) trees.Add(actor.User.AccessTree);
        return permitted(new TypeAuthContext(trees, typeof(ShiftIdentityActions)));
    }

    private static AuthenticationRefused? AdminTargetRefusal(IdentityAdmissionServices services, IdentitySecurityTransaction actor,
        IdentitySecurityTransaction unit, AdminActor who)
    {
        var refusal = ActorRefusal(services, actor, who, permissions => permissions.CanWrite(ShiftIdentityActions.Users));
        if (refusal is not null) return refusal;
        if (unit.User.IsDeleted) return Refuse(AuthenticationFailure.AccountUnavailable);
        return unit.User.IsProtected ? Refuse(AuthenticationFailure.ClientDenied) : null;
    }
}
