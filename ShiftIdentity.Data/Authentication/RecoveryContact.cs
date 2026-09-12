using System.Net.Mail;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public enum RecoveryEmailProvenance { Unknown = 0, TrustedAdminAssignment = 1, AuthenticatedContactChange = 2, OwnershipVerification = 3, VerifiedLegacyMigration = 4 }

/// <summary>These methods record authority supplied by a caller that already performed its proof checks.</summary>
public static class RecoveryContact
{
    public static string Key(string? value) => (value ?? "").Trim().ToUpperInvariant();

    /// <summary>Explicit seed/migration initialization. This does not establish recovery eligibility.</summary>
    public static void InitializeLookup(User user, UserSecurityState security)
    {
        RequireSubject(user, security);
        if (security.UsernameLookupKey is not null || security.EmailLookupKey is not null)
        {
            if (!LookupMatches(user, security))
                throw new InvalidOperationException("Existing lookup keys require an authorized mutation.");
            return;
        }
        if (user.IsDeleted) return;
        var username = Key(user.Username);
        var email = Key(user.Email);
        if (username.Length is 0 or > 255 || email.Length > 255)
            throw new ArgumentException("Valid saved identity fields are required.");
        security.UsernameLookupKey = username;
        security.EmailLookupKey = email.Length == 0 ? null : email;
    }

    public static bool LookupMatches(User user, UserSecurityState security) =>
        user.ID == security.UserID && !user.IsDeleted &&
        security.UsernameLookupKey is { Length: > 0 } && security.UsernameLookupKey == Key(user.Username) &&
        security.EmailLookupKey == (string.IsNullOrWhiteSpace(user.Email) ? null : Key(user.Email));

    public static bool IsEligible(User user, UserSecurityState security) =>
        LookupMatches(user, security) && !string.IsNullOrWhiteSpace(user.Email) && security.RecoveryEmailRevision == security.ContactRevision &&
        (security.RecoveryEmailProvenance is RecoveryEmailProvenance.TrustedAdminAssignment or RecoveryEmailProvenance.AuthenticatedContactChange or
            RecoveryEmailProvenance.OwnershipVerification or RecoveryEmailProvenance.VerifiedLegacyMigration) &&
        string.Equals(security.RecoveryEmail, user.Email.Trim(), StringComparison.Ordinal);

    public static void RecordOwnership(User user, UserSecurityState security, RecoveryEmailProvenance provenance)
    {
        if (!LookupMatches(user, security) || provenance == RecoveryEmailProvenance.Unknown || !Enum.IsDefined(provenance) || string.IsNullOrWhiteSpace(user.Email))
            throw new ArgumentException("Initialized saved contact authority and explicit provenance are required.");
        security.RecoveryEmail = user.Email.Trim();
        security.RecoveryEmailRevision = security.ContactRevision;
        security.RecoveryEmailProvenance = provenance;
    }

    // A migration must deliberately attest the original verified address before any legacy writer can run.
    public static void PreserveVerifiedLegacyEmail(User user, UserSecurityState security)
    {
        if (user.EmailVerified && !string.IsNullOrWhiteSpace(user.Email))
            RecordOwnership(user, security, RecoveryEmailProvenance.VerifiedLegacyMigration);
    }

    public static void Invalidate(UserSecurityState security)
    {
        security.RecoveryEmail = null; security.RecoveryEmailRevision = null;
        security.RecoveryEmailProvenance = RecoveryEmailProvenance.Unknown;
    }

    /// <summary>
    /// Applies an already authorized email change inside the caller's admission transaction.
    /// The caller owns actor permissions, fresh proof and the target's eligibility (for example whether an
    /// inactive account may be changed). This helper supplies no authentication.
    /// </summary>
    public static bool ApplyAuthorizedEmailChange(IdentitySecurityTransaction unit, long expectedVersion,
        long expectedContactRevision, string? email, RecoveryEmailProvenance provenance, DateTimeOffset now)
    {
        var user = unit.User;
        var security = unit.Security;
        if (!LookupMatches(user, security) || user.IsDeleted || security.SecurityVersion != expectedVersion || security.ContactRevision != expectedContactRevision)
            throw new IdentitySecurityConflictException("Contact authority changed.");
        if (provenance is not (RecoveryEmailProvenance.TrustedAdminAssignment or RecoveryEmailProvenance.AuthenticatedContactChange))
            throw new ArgumentException("A contact change requires existing assignment or authentication authority.");
        email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (email is not null && (email.Length > 255 || email.Any(char.IsControl) || !MailAddress.TryCreate(email, out var address) ||
            address.Address != email || !string.IsNullOrEmpty(address.DisplayName)))
            throw new ArgumentException("Invalid email address.");
        if (Key(user.Email) == Key(email)) return false;

        var version = checked(security.SecurityVersion + 1);
        var revision = checked(security.ContactRevision + 1);
        user.Email = email;
        user.EmailVerified = false;
        user.VerificationSASToken = null;
        security.SecurityVersion = version;
        security.ContactRevision = revision;
        security.EmailLookupKey = email is null ? null : Key(email);
        Invalidate(security);
        if (email is not null) RecordOwnership(user, security, provenance);
        foreach (var link in unit.Links)
        {
            link.State = AuthenticationOperationState.Superseded; link.CompletedAt = now;
            link.HandleDigest = []; link.OutstandingLinkSlot = null; link.Destination = null;
        }
        unit.Audit("EmailContactChanged", now);
        return true;
    }

    private static void RequireSubject(User user, UserSecurityState security)
    {
        if (user.ID <= 0 || user.ID != security.UserID)
            throw new ArgumentException("The contact and security state must belong to the same saved user.");
    }
}
