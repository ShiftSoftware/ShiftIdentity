using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ShiftSoftware.ShiftIdentity.Core.Authentication;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public sealed partial class SqlIdentitySecurityStore
{
    public async Task<long?> FindProviderUserAsync(SignInProvider provider, string tenantID, string objectID,
        string? emailLookupKey, CancellationToken ct)
    {
        try
        {
            // A link counts only while its account still has the email it matched; a stale link falls through to the email.
            var linked = await (from link in db.Set<UserProviderLink>().AsNoTracking()
                                join security in db.Set<UserSecurityState>().AsNoTracking() on link.UserID equals security.UserID
                                join user in db.Users.IgnoreQueryFilters().AsNoTracking() on link.UserID equals user.ID
                                where link.Provider == provider && link.TenantID == tenantID && link.ObjectID == objectID &&
                                    security.EmailLookupKey == link.EmailLookupKey && !user.IsDeleted
                                select (long?)link.UserID).SingleOrDefaultAsync(ct);
            if (linked is not null || string.IsNullOrEmpty(emailLookupKey)) return linked;
            var matches = await (from security in db.Set<UserSecurityState>().AsNoTracking()
                                 join user in db.Users.IgnoreQueryFilters().AsNoTracking() on security.UserID equals user.ID
                                 where security.EmailLookupKey == emailLookupKey && !user.IsDeleted
                                 select security.UserID).Take(2).ToListAsync(ct);
            return matches.Count == 1 ? matches[0] : null;
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity store unavailable.", e); }
    }

    public async Task<UserProviderLink?> ReadProviderLinkAsync(SignInProvider provider, string tenantID, string objectID, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A provider link is read for change only inside admission.");
        try
        {
            // Names come from the EF model and the columns are listed, as in Hinted: a temporal mapping hides its period columns from SELECT *.
            var entity = db.Model.FindEntityType(typeof(UserProviderLink))!;
            var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            string Column(string property) => entity.FindProperty(property)!.GetColumnName(table)!;
            var columns = string.Join(", ", entity.GetProperties().Select(x => x.GetColumnName(table))
                .Where(x => x is not null).Distinct(StringComparer.Ordinal).Select(x => $"[{x}]"));
            return await db.Set<UserProviderLink>().FromSqlRaw(
                    $"SELECT {columns} FROM [{entity.GetSchema() ?? "dbo"}].[{table.Name}] WITH (UPDLOCK, HOLDLOCK) " +
                    $"WHERE [{Column(nameof(UserProviderLink.Provider))}] = {{0}} AND [{Column(nameof(UserProviderLink.TenantID))}] = {{1}} " +
                    $"AND [{Column(nameof(UserProviderLink.ObjectID))}] = {{2}}",
                    (int)provider, tenantID, objectID)
                .SingleOrDefaultAsync(ct);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity store unavailable.", e); }
    }

    public void AddProviderLink(UserProviderLink link)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A provider link is added only inside admission.");
        db.Set<UserProviderLink>().Add(link);
    }

    public void RemoveProviderLink(UserProviderLink link)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A provider link is removed only inside admission.");
        db.Set<UserProviderLink>().Remove(link);
    }

    public async Task<IReadOnlyList<UserProviderLink>> ReadProviderLinksAsync(long userID, CancellationToken ct)
    {
        try
        {
            return await db.Set<UserProviderLink>().AsNoTracking().Where(x => x.UserID == userID)
                .OrderBy(x => x.CreatedAt).ToListAsync(ct);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity store unavailable.", e); }
    }
}
