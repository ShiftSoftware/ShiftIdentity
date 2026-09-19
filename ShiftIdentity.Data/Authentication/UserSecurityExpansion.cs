using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

/// <summary>
/// Creates the security rows of users the authority has not seen: the users that existed before the host enabled
/// the authority, and users a legacy path inserts afterwards (the built-in seed, the live-data sync). A row starts at
/// version 1 with its lookup keys initialized from the saved username and email, and a verified saved address is
/// attested as recovery-eligible legacy data. This is the explicit expansion of the row the schema rule requires,
/// run by the authority's startup and by those legacy paths; an admission never defaults a missing row.
/// </summary>
public static class UserSecurityExpansion
{
    public static UserSecurityState CreateFor(User user)
    {
        var state = new UserSecurityState { UserID = user.ID };
        try { RecoveryContact.InitializeLookup(user, state); }
        catch (ArgumentException)
        {
            // A saved username that normalizes to nothing, or an over-long identifier: the row still exists and the
            // account still signs in by its exact username; it is not reachable by the security-email lookup until an
            // admitted identifier change initializes the keys.
            state.UsernameLookupKey = null; state.EmailLookupKey = null;
        }
        if (RecoveryContact.LookupMatches(user, state)) RecoveryContact.PreserveVerifiedLegacyEmail(user, state);
        return state;
    }

    /// <summary>
    /// Creates a row for every user (active, inactive or deleted) that has none, in bounded batches. A row another
    /// instance created meanwhile is skipped. A user whose lookup keys collide with an existing row's (two saved
    /// identifiers that normalize to the same key) gets its row without keys and is reported through
    /// <paramref name="degraded"/>: the account signs in by its exact username but is not reachable by the
    /// security-email lookup until an administrator corrects the identifiers.
    /// </summary>
    public static async Task<int> ExpandMissingAsync(ShiftIdentityDbContext db, Action<long>? degraded = null, CancellationToken ct = default)
    {
        var created = 0;
        long after = 0;
        while (true)
        {
            db.ChangeTracker.Clear();
            var users = await db.Users.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.ID > after && !db.Set<UserSecurityState>().Any(s => s.UserID == u.ID))
                .OrderBy(u => u.ID).Take(200).ToListAsync(ct);
            if (users.Count == 0) return created;
            after = users[^1].ID;
            foreach (var user in users) db.Set<UserSecurityState>().Add(CreateFor(user));
            try { created += await db.SaveChangesAsync(ct); continue; }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }
            // The batch did not save as a whole: settle each row on its own.
            foreach (var user in users)
            {
                if (await db.Set<UserSecurityState>().AnyAsync(s => s.UserID == user.ID, ct)) continue;
                var state = CreateFor(user);
                db.Set<UserSecurityState>().Add(state);
                try { created += await db.SaveChangesAsync(ct); }
                catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 })
                {
                    // A lookup key collides with another row's. Keep the row, drop the keys, report the user.
                    db.ChangeTracker.Clear();
                    state = new UserSecurityState { UserID = user.ID };
                    db.Set<UserSecurityState>().Add(state);
                    created += await db.SaveChangesAsync(ct);
                    degraded?.Invoke(user.ID);
                }
                catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2627 })
                {
                    // Another instance created this row first.
                    db.ChangeTracker.Clear();
                }
            }
        }
    }
}
