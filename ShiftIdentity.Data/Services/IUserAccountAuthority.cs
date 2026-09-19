using ShiftSoftware.ShiftIdentity.Core.Models;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Services;

/// <summary>
/// The staged v2 authority behind the legacy administrator writers: the attribute-driven User save and delete on
/// <c>api/IdentityUser</c>, <c>AssignRandomPasswords</c>, <c>VerifyPhones</c>, <c>ResetTotp</c> and user import.
/// <para>
/// When a host registers this service, those writers no longer change a credential, identifier, contact, status,
/// permission or deletion flag on their own. They describe the change, and <see cref="Repositories.UserRepository"/>
/// asks the authority to admit it inside the repository's own transaction, just before the flush. The authority locks
/// the policy row, the App row, the operator and every target (ascending user ID order), checks the operator and each
/// target against current state, applies the change to the tracked entities, increments each changed target's
/// SecurityVersion once and records operator-attributed audit rows. The repository then flushes and commits, so the
/// ordinary profile fields and the security changes land together or not at all: there is no partial save.
/// </para>
/// <para>
/// A host without this registration keeps the previous direct writes. That is the production state until the
/// authority cutover; the legacy path is unchanged there.
/// </para>
/// </summary>
public interface IUserAccountAuthority
{
    /// <summary>
    /// Checks a new password against the shared new-password policy. Returns null when it passes, otherwise a
    /// message key describing the failure.
    /// </summary>
    string? CheckNewPassword(string password, string username);

    /// <summary>
    /// Admits the pending changes inside the repository's open transaction on <paramref name="db"/>. A refusal throws
    /// a <c>ShiftEntityException</c> that carries the response message; nothing is flushed or committed here.
    /// </summary>
    Task AdmitAsync(ShiftIdentityDbContext db, IReadOnlyList<UserAccountChange> changes, CancellationToken cancellationToken);

    /// <summary>
    /// Records the security state, lookup keys, contact provenance and creation audit for users the repository
    /// inserted in the same transaction, after their IDs exist. The rows are flushed here; the repository commits.
    /// </summary>
    Task RegisterCreatedAsync(ShiftIdentityDbContext db, IReadOnlyList<UserAccountCreation> created, CancellationToken cancellationToken);

    /// <summary>
    /// After the commit: asks the staged delivery path for a verification link to the user's saved address, on
    /// behalf of the same operator. This runs outside every SQL transaction and lock.
    /// </summary>
    Task<UserAccountDelivery> RequestVerificationAsync(long userID, CancellationToken cancellationToken);
}

/// <summary>What the staged delivery path reported for a post-commit verification request.</summary>
public enum UserAccountDelivery
{
    /// <summary>The host accepted the message, or the account was not eligible; both look the same to the caller.</summary>
    Requested = 1,
    /// <summary>The host did not confirm that it accepted the message. The operator can send it again later.</summary>
    Unconfirmed = 2,
    /// <summary>The request could not be made, for example because the operator's session was not accepted.</summary>
    NotRequested = 3
}

/// <summary>
/// One target of an administrator write and the parts of it that need admission. A part that is not set is
/// unchanged. The tracked <see cref="User"/> already carries the ordinary field edits; the authority applies the
/// parts listed here to that same instance, so one flush writes everything.
/// </summary>
public sealed class UserAccountChange
{
    public required User User { get; init; }

    /// <summary>A new credential, hashed before the transaction started. Null when no password was supplied.</summary>
    public HashModel? Password { get; init; }

    /// <summary>Applies only with <see cref="Password"/>: force a change at the next sign-in.</summary>
    public bool RequireChangeAtNextLogin { get; init; } = true;

    /// <summary>The requested username when it differs from the saved one; otherwise null.</summary>
    public string? Username { get; init; }

    public bool EmailChanged { get; init; }
    public string? Email { get; init; }

    public bool PhoneChanged { get; init; }
    /// <summary>The formatted phone, or null to remove it. Read only when <see cref="PhoneChanged"/> is true.</summary>
    public string? Phone { get; init; }

    /// <summary>The requested active status when it differs from the saved one; otherwise null.</summary>
    public bool? IsActive { get; init; }

    /// <summary>
    /// True when the generated access tree or the assigned access trees differ from what is saved. The M:N rows are
    /// already tracked as added or removed; <see cref="AccessTree"/> is the generated user-specific tree to store.
    /// </summary>
    public bool PermissionsChanged { get; init; }
    public string? AccessTree { get; init; }

    /// <summary>Mark the saved phone as verified. This restricts nothing, so it does not increment the version.</summary>
    public bool VerifyPhone { get; init; }

    /// <summary>The row is being soft-deleted; the repository default already set the flag.</summary>
    public bool Delete { get; init; }

    /// <summary>
    /// Disable the active authenticator and require individual recovery (the bulk <c>ResetTotp</c> route). No recovery
    /// code is issued here; an account without an active factor is left unchanged.
    /// </summary>
    public bool ResetAuthenticator { get; init; }

    public bool IsEmpty => Password is null && Username is null && !EmailChanged && !PhoneChanged && IsActive is null &&
        !PermissionsChanged && !VerifyPhone && !Delete && !ResetAuthenticator;
}

/// <summary>A user the repository inserted in the current transaction.</summary>
public sealed record UserAccountCreation(User User);
