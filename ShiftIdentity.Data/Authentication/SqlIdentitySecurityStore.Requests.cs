using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public sealed partial class SqlIdentitySecurityStore
{
    public async Task<SecurityEmailLookup?> ResolveSecurityEmailAsync(string identifier, CancellationToken ct)
    {
        try
        {
            var key = RecoveryContact.Key(identifier);
            if (key.Length is 0 or > 255) return null;
            // Keys are explicitly initialized with the opted-in authority, never inferred on a request.
            var ids = await db.Set<UserSecurityState>().AsNoTracking()
                .Where(x => x.UsernameLookupKey == key || x.EmailLookupKey == key)
                .Select(x => x.UserID).Take(2).ToArrayAsync(ct);
            if (ids.Length == 1) return new(ids[0], key);
            if (ids.Length > 1)
            {
                db.Set<AuthenticationAuditEvent>().Add(new() { Outcome = "AmbiguousSecurityEmailRequest", CreatedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            return null;
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity lookup unavailable.", e); }
        catch (DbUpdateException e) { throw new IdentitySecurityUnavailableException("Identity lookup audit unavailable.", e); }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task<bool> RecheckSecurityEmailAsync(SecurityEmailLookup lookup, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Security-email lookup must be checked inside admission.");
        try
        {
            // Range locks on both unique indexes prevent a cross-field match being introduced before commit.
            var usernames = await db.Set<UserSecurityState>().FromSqlInterpolated($"SELECT * FROM [ShiftIdentity].[UserSecurityStates] WITH (HOLDLOCK, INDEX(IX_UserSecurityStates_UsernameLookupKey)) WHERE [UsernameLookupKey] = {lookup.Key}")
                .AsNoTracking().ToArrayAsync(ct);
            var emails = await db.Set<UserSecurityState>().FromSqlInterpolated($"SELECT * FROM [ShiftIdentity].[UserSecurityStates] WITH (HOLDLOCK, INDEX(IX_UserSecurityStates_EmailLookupKey)) WHERE [EmailLookupKey] = {lookup.Key}")
                .AsNoTracking().ToArrayAsync(ct);
            var matches = usernames.Concat(emails).DistinctBy(x => x.UserID).ToArray();
            if (matches.Length != 1 || matches[0].UserID != lookup.UserID)
            {
                if (matches.Length > 1)
                    db.Set<AuthenticationAuditEvent>().Add(new() { Outcome = "AmbiguousSecurityEmailRequest", CreatedAt = DateTimeOffset.UtcNow });
                return false;
            }
            var user = await db.Users.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.ID == lookup.UserID, ct);
            return user is not null && RecoveryContact.LookupMatches(user, matches[0]) &&
                (RecoveryContact.Key(user.Username) == lookup.Key || RecoveryContact.Key(user.Email) == lookup.Key);
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Identity lookup recheck unavailable.", e); }
    }

    public async Task<bool> ConsumeIngressAsync(string key, DateTimeOffset now, int limit, TimeSpan window, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            // Serialize creation as well as increments, across processes. The key is a digest, never a raw IP address.
            var resource = "IdentityIngress:" + key;
            await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @r < 0 THROW 51000, 'Throttle lock unavailable.', 1;", ct);
            var bucket = await db.Set<AuthThrottleBucket>().SingleOrDefaultAsync(x => x.Key == key, ct);
            if (bucket is null) { bucket = new() { Key = key, WindowStart = now }; db.Add(bucket); }
            if (now >= bucket.WindowStart + window) { bucket.WindowStart = now; bucket.Count = 0; }
            var allowed = now >= bucket.WindowStart && bucket.Count < limit;
            if (allowed) bucket.Count++;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return allowed;
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Delivery limit unavailable.", e); }
        catch (DbUpdateException e) { throw new IdentitySecurityUnavailableException("Delivery limit unavailable.", e); }
        finally { db.ChangeTracker.Clear(); }
    }

}
