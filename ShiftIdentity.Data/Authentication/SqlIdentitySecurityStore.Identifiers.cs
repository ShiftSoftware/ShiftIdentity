using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public sealed partial class SqlIdentitySecurityStore
{
    /// <summary>
    /// Range-locks both lookup-key indexes for the proposed value, so a concurrent assignment of the same key
    /// waits for this admission to commit; the unique indexes remain the final guard. Saved username/email
    /// columns cover rows whose keys were never initialized.
    /// </summary>
    public async Task<bool> IdentifierInUseAsync(string value, long exceptUserID, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Identifier uniqueness must be checked inside admission.");
        var key = RecoveryContact.Key(value);
        if (key.Length is 0 or > 255) return true;
        try
        {
            var usernames = await Hinted<UserSecurityState>("HOLDLOCK", nameof(UserSecurityState.UsernameLookupKey), key, indexOn: nameof(UserSecurityState.UsernameLookupKey))
                .AsNoTracking().ToArrayAsync(ct);
            var emails = await Hinted<UserSecurityState>("HOLDLOCK", nameof(UserSecurityState.EmailLookupKey), key, indexOn: nameof(UserSecurityState.EmailLookupKey))
                .AsNoTracking().ToArrayAsync(ct);
            if (usernames.Concat(emails).Any(x => x.UserID != exceptUserID)) return true;
            var trimmed = value.Trim();
            return await db.Users.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => !x.IsDeleted && x.ID != exceptUserID && (x.Username == trimmed || x.Email == trimmed), ct);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identifier check unavailable.", e); }
    }
}
