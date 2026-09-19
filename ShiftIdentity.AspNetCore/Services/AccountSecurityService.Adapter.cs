using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.AspNetCore.Authentication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Authentication;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Authentication;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Services;
using static ShiftSoftware.ShiftIdentity.AspNetCore.Authentication.AdmissionRules;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Services;

/// <summary>Why the legacy adapter refused a save. The host translates it into the response envelope.</summary>
internal sealed class AdmissionRefusedException(AuthenticationFailure code, AdmissionRefusalReason reason, string? field = null)
    : Exception($"Admission refused: {code} ({reason}).")
{
    public AuthenticationFailure Code { get; } = code;
    public AdmissionRefusalReason Reason { get; } = reason;
    /// <summary>The DTO property the refusal is about, when there is one.</summary>
    public string? Field { get; } = field;
}

internal enum AdmissionRefusalReason { Operator, Self, Protected, Deleted, Stale, Duplicate, Invalid, Unavailable }

/// <summary>
/// The v2 boundary for the legacy administrator writers. One admission per repository save: the operator and every
/// target are locked in ID order inside the repository's transaction, each target's tracked row is checked against
/// the database under the lock, the requested changes are applied through the shared mutation bodies, each changed
/// target's SecurityVersion is incremented once and every change is audited with the operator.
/// </summary>
internal static partial class AccountSecurityService
{
    /// <summary>
    /// Identifies the operator of the current request. A staged v2 access token is validated strictly and gives a full
    /// proof; a legacy host-authenticated principal gives only its user ID. A v2 token the strict validator refuses
    /// never falls back to the host's principal.
    /// </summary>
    internal static AdminActor? ReadActor(IdentityAdmissionServices services, HttpContext? http)
    {
        if (http is null) return null;
        var session = ReadSignedIn(services, http.Request.Headers.Authorization.ToString());
        if (session is not null) return new(session.Proof.UserID, session);
        if (http.User.FindFirst(AdmissionTokenCodec.Schema) is not null) return null;
        var id = http.User.GetUserID(services.HashIds);
        return id is > 0 ? new(id.Value, null) : null;
    }

    internal static async Task AdmitLegacyAsync(IdentityAdmissionServices services, ShiftIdentityDbContext db, AdminActor who,
        IReadOnlyList<UserAccountChange> changes, CancellationToken ct)
    {
        services.Observe?.Invoke("LegacyAdmission");
        var store = SharedStore(services, db);
        var units = await store.AdmitWithinAsync(changes.Select(x => x.User.ID).Append(who.UserID), services.Client, ct);
        services.Observe?.Invoke("AdmissionLock");
        var actor = units[who.UserID];
        var now = services.Clock.GetUtcNow();
        foreach (var change in changes)
        {
            var unit = units[change.User.ID];
            if (!ReferenceEquals(unit.User, change.User))
                throw new InvalidOperationException("The admitted user is not the tracked row of this save.");
            var refusal = ActorRefusal(services, actor, who, permissions =>
                change.Delete ? permissions.CanDelete(ShiftIdentityActions.Users) : permissions.CanWrite(ShiftIdentityActions.Users));
            if (refusal is not null) throw new AdmissionRefusedException(refusal.Code, AdmissionRefusalReason.Operator);
            if (change.User.ID == who.UserID) throw new AdmissionRefusedException(AuthenticationFailure.ClientDenied, AdmissionRefusalReason.Self);
            if (unit.User.IsProtected) throw new AdmissionRefusedException(AuthenticationFailure.ClientDenied, AdmissionRefusalReason.Protected);
            if (!change.Delete && unit.User.IsDeleted) throw new AdmissionRefusedException(AuthenticationFailure.AccountUnavailable, AdmissionRefusalReason.Deleted);
            // The row as loaded must still be the row in the database: a change that landed after the form loaded
            // it (a self-service change, another operator, a reset) is never overwritten.
            if (!await RowUnchangedAsync(db, change.User, ct)) throw new AdmissionRefusedException(AuthenticationFailure.StaleOperation, AdmissionRefusalReason.Stale);
            if (services.Options.PolicyRevision != unit.Policy.Revision) throw new AdmissionRefusedException(AuthenticationFailure.Unavailable, AdmissionRefusalReason.Unavailable);

            var startVersion = unit.Security.SecurityVersion;
            var audits = new List<string>();
            var restrictive = false;
            if (change.IsActive is { } active && ApplyStatus(unit, active))
            {
                audits.Add(active ? "AccountActivated" : "AccountDeactivated");
                restrictive = true;
            }
            if (change.Username is { } username)
            {
                var (failure, applied) = await ApplyUsernameAsync(services, unit, username, ct);
                if (failure is not null) throw new AdmissionRefusedException(failure.Code, Reason(failure.Code), nameof(User.Username));
                if (applied) { audits.Add("AdminUsernameChanged"); restrictive = true; }
            }
            if (change.EmailChanged)
            {
                var (failure, applied) = await ApplyEmailAsync(services, unit, change.Email, now, ct);
                if (failure is not null) throw new AdmissionRefusedException(failure.Code, Reason(failure.Code), nameof(User.Email));
                if (applied) audits.Add("AdminEmailChanged");
            }
            if (change.PhoneChanged && ApplyPhone(unit, change.Phone))
            {
                audits.Add("AdminPhoneChanged");
                restrictive = true;
            }
            if (change.PermissionsChanged)
            {
                unit.User.AccessTree = change.AccessTree;
                audits.Add("AdminPermissionsChanged");
                restrictive = true;
            }
            if (change.Password is { } candidate)
            {
                ApplyPassword(unit, candidate, change.RequireChangeAtNextLogin);
                audits.Add(PasswordAudit(change.RequireChangeAtNextLogin));
                restrictive = true;
            }
            if (change.VerifyPhone && !string.IsNullOrWhiteSpace(unit.User.Phone) && !unit.User.PhoneVerified)
            {
                // Marking a saved contact verified restricts nothing, so the version stays.
                unit.User.PhoneVerified = true;
                audits.Add("AdminPhoneVerified");
            }
            if (change.ResetAuthenticator && ApplyAuthenticatorReset(unit, now))
            {
                audits.Add("MfaReset");
                restrictive = true;
            }
            if (change.Delete)
            {
                if (!unit.User.IsDeleted) throw new InvalidOperationException("The repository default must flag the row before admission.");
                foreach (var link in unit.Links)
                {
                    AdmissionOperations.Finish(link, now, cancelled: true);
                    link.State = AuthenticationOperationState.Superseded;
                }
                audits.Add("AccountDeleted");
                restrictive = true;
            }
            // One increment per target per save; an email change already advanced the version inside its body.
            if (restrictive && unit.Security.SecurityVersion == startVersion) Bump(unit);
            foreach (var audit in audits) unit.Audit(audit, now, actorUserID: who.UserID);
        }
        services.Observe?.Invoke("LegacyMutation");
    }

    /// <summary>Records the security state of rows the repository inserted in this transaction, under the operator's lock.</summary>
    internal static async Task RegisterCreatedAsync(IdentityAdmissionServices services, ShiftIdentityDbContext db, AdminActor who,
        IReadOnlyList<UserAccountCreation> created, CancellationToken ct)
    {
        var store = SharedStore(services, db);
        var units = await store.AdmitWithinAsync([who.UserID], services.Client, ct);
        var refusal = ActorRefusal(services, units[who.UserID], who, permissions => permissions.CanWrite(ShiftIdentityActions.Users));
        if (refusal is not null) throw new AdmissionRefusedException(refusal.Code, AdmissionRefusalReason.Operator);
        var now = services.Clock.GetUtcNow();
        foreach (var creation in created)
        {
            var user = creation.User;
            if (user.ID <= 0) throw new InvalidOperationException("A created user must be flushed before its security state is recorded.");
            var state = new UserSecurityState { UserID = user.ID };
            RecoveryContact.InitializeLookup(user, state);
            // An address assigned by an administrator is recovery-eligible; a verified import keeps its verified flag.
            if (!string.IsNullOrWhiteSpace(user.Email))
                RecoveryContact.RecordOwnership(user, state, RecoveryEmailProvenance.TrustedAdminAssignment);
            db.Set<UserSecurityState>().Add(state);
            db.Set<AuthenticationAuditEvent>().Add(new()
            {
                UserID = user.ID, SecurityVersion = state.SecurityVersion, Outcome = "AccountCreated", CreatedAt = now, ActorUserID = who.UserID
            });
        }
        await db.SaveChangesAsync(ct);
    }

    private static SqlIdentitySecurityStore SharedStore(IdentityAdmissionServices services, ShiftIdentityDbContext db) =>
        services.Store is SqlIdentitySecurityStore store && store.Shares(db) ? store
            : throw new InvalidOperationException("The legacy writers and the staged authority must share one identity context.");

    private static AdmissionRefusalReason Reason(AuthenticationFailure code) => code switch
    {
        AuthenticationFailure.DuplicateIdentifier => AdmissionRefusalReason.Duplicate,
        AuthenticationFailure.InvalidRequest => AdmissionRefusalReason.Invalid,
        AuthenticationFailure.Unavailable => AdmissionRefusalReason.Unavailable,
        _ => AdmissionRefusalReason.Stale
    };

    // Compares the tracked row's original values with the database row read under the lock.
    private static async Task<bool> RowUnchangedAsync(ShiftIdentityDbContext db, User user, CancellationToken ct)
    {
        var row = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.ID == user.ID).Select(x => new
        {
            x.Username, x.Email, x.Phone, x.IsActive, x.IsDeleted, x.IsProtected, x.RequireChangePassword,
            x.EmailVerified, x.PhoneVerified, x.AccessTree, x.PasswordHash, x.Salt, x.VerificationSASToken
        }).SingleOrDefaultAsync(ct);
        if (row is null) return false;
        var original = db.Entry(user).OriginalValues;
        return original.GetValue<string>(nameof(User.Username)) == row.Username &&
            original.GetValue<string?>(nameof(User.Email)) == row.Email &&
            original.GetValue<string?>(nameof(User.Phone)) == row.Phone &&
            original.GetValue<bool>(nameof(User.IsActive)) == row.IsActive &&
            original.GetValue<bool>(nameof(User.IsDeleted)) == row.IsDeleted &&
            original.GetValue<bool>(nameof(User.IsProtected)) == row.IsProtected &&
            original.GetValue<bool>(nameof(User.RequireChangePassword)) == row.RequireChangePassword &&
            original.GetValue<bool>(nameof(User.EmailVerified)) == row.EmailVerified &&
            original.GetValue<bool>(nameof(User.PhoneVerified)) == row.PhoneVerified &&
            original.GetValue<string?>(nameof(User.AccessTree)) == row.AccessTree &&
            original.GetValue<string?>(nameof(User.VerificationSASToken)) == row.VerificationSASToken &&
            (original.GetValue<byte[]>(nameof(User.PasswordHash)) ?? []).AsSpan().SequenceEqual(row.PasswordHash) &&
            (original.GetValue<byte[]>(nameof(User.Salt)) ?? []).AsSpan().SequenceEqual(row.Salt);
    }
}
