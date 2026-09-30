using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.TypeAuth.Core;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

/// <summary>
/// Turning MFA off where the host's policy makes it optional. An administrator turns it off for another account with
/// the guard of an MFA recovery code: the Manage MFA Recovery permission, a recent sign-in and a recorded identity-check
/// note. A user turns off their own with a current code from it. Either way the authenticator, a pending recovery and
/// its code are removed, every session of the account ends, and the account signs in with the password only until it
/// sets up an authenticator again. Lost-device recovery is separate and keeps MFA on.
/// </summary>
internal static partial class AccountSecurityService
{
    internal static Task<AuthOutcome> TurnOffAccountMfaAsync(IdentityAdmissionServices services, string? authorization,
        AdminMfaTurnOffRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request) || string.IsNullOrWhiteSpace(request.VerificationReference) || request.VerificationReference.Any(char.IsControl))
            return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        // Operators turn off their own MFA from their profile, with a code from it.
        if (signedIn.Proof.UserID == request.UserID) return Refuse(AuthenticationFailure.ClientDenied);
        return await services.Store.AdmitAdminAsync<AuthOutcome>(signedIn.Proof.UserID, request.UserID, services.Client, (actor, unit) =>
        {
            var refusal = ActorRefusal(services, actor, signedIn, permissions => permissions.CanAccess(ShiftIdentityActions.ManageMfaRecovery));
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            // An inactive account can be corrected before it is activated again; a deleted one is not changed.
            if (unit.User.IsDeleted) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AccountUnavailable));
            if (MfaMandatory(unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            var now = services.Clock.GetUtcNow();
            if (!ApplyMfaTurnOff(unit, now))
                return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Mfa, false, unit.Security.SecurityVersion));
            Bump(unit);
            unit.Audit("MfaTurnedOff", now, actorUserID: actor.User.ID, verificationReference: request.VerificationReference.Trim());
            services.Observe?.Invoke("AdminAccountMutation");
            return Task.FromResult<AuthOutcome>(new AdminAccountChanged(AdminAccountChange.Mfa, true, unit.Security.SecurityVersion));
        }, ct);
    });

    internal static Task<AuthOutcome> TurnOffOwnMfaAsync(IdentityAdmissionServices services, string? authorization,
        TurnOffMfaRequest request, CancellationToken ct) => AtBoundary(async () =>
    {
        if (!Valid(request)) return Refuse(AuthenticationFailure.InvalidRequest);
        var signedIn = ReadSignedIn(services, authorization);
        if (signedIn is null) return Refuse(AuthenticationFailure.InvalidGrant);
        return await services.Store.AdmitAsync<AuthOutcome>(signedIn.Proof.UserID, null, services.Client, unit =>
        {
            var refusal = SignedInRefusal(services, unit, signedIn);
            if (refusal is not null) return Task.FromResult<AuthOutcome>(refusal);
            var now = services.Clock.GetUtcNow();
            if (unit.Security.LocalMfaRecoveryRequired) return Task.FromResult<AuthOutcome>(Restricted(AuthenticationStep.MfaRecovery, now));
            if (unit.User.RequireChangePassword) return Task.FromResult<AuthOutcome>(Restricted(AuthenticationStep.PasswordChange, now));
            if (unit.Security.ProtectedTotpSecret is null) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.StaleOperation));
            if (MfaMandatory(unit)) return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.ClientDenied));
            if (BudgetExhausted(unit.Security, now) || unit.User.LockDownUntil > now.UtcDateTime)
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.AttemptsExhausted));
            if (!AdmissionOperations.ActiveFactorAccepts(services, unit, request.Code, now, out _))
            {
                // No operation holds this proof, so a wrong code counts against the account's own budget.
                FailedProof(unit.Security, now);
                unit.Audit("InvalidMfa", now);
                return Task.FromResult<AuthOutcome>(Refuse(AuthenticationFailure.InvalidProof));
            }
            ApplyMfaTurnOff(unit, now);
            Bump(unit);
            unit.Audit("MfaTurnedOff", now);
            services.Observe?.Invoke("MfaMutation");
            // Only this device continues, with a session proven by the code just entered, as after a replacement.
            var remaining = LocalStep(unit, true);
            return Task.FromResult<AuthOutcome>(new MfaChanged(remaining is { } next ? Restricted(next, now)
                : Issue(services, unit, Proof(unit, services, true, now), now)));
        }, ct);
    });

    /// <summary>
    /// Removes the authenticator and any recovery in progress: the factor generation advances, the protected secret and
    /// its replay state are cleared, an outstanding recovery code and its continuation are superseded, and recovery is
    /// no longer required. The retained plaintext column is cleared too, so neither the startup copy nor the legacy
    /// sign-in brings the factor back. The caller checks the policy, increments the version once and writes the audit
    /// row. Returns false when there was nothing to turn off.
    /// </summary>
    internal static bool ApplyMfaTurnOff(IdentitySecurityTransaction unit, DateTimeOffset now)
    {
        var security = unit.Security;
        if (security.ProtectedTotpSecret is null && !security.LocalMfaRecoveryRequired && security.MfaRecoveryOperationID is null &&
            unit.User.TotpSecret is null) return false;
        SupersedeRecovery(unit, now);
        security.MfaRecoveryOperationID = null;
        security.FactorGeneration = checked(security.FactorGeneration + 1);
        security.ProtectedTotpSecret = null;
        security.LastAcceptedTotpStep = null;
        security.TotpProtectionVersion = 0;
        security.LocalMfaRecoveryRequired = false;
        unit.User.TotpSecret = null;
        return true;
    }
}
