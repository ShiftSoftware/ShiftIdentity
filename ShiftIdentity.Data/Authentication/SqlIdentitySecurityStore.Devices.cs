using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public sealed partial class SqlIdentitySecurityStore
{
    public async Task<bool> IngressExhaustedAsync(string key, DateTimeOffset now, int limit, TimeSpan window, CancellationToken ct)
    {
        try
        {
            var bucket = await db.Set<AuthThrottleBucket>().AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct);
            return bucket is not null && now >= bucket.WindowStart && now < bucket.WindowStart + window && bucket.Count >= limit;
        }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Throttle state unavailable.", e); }
    }

    public async Task<bool> AddDeviceAuthorizationAsync(DeviceAuthorization authorization, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        try
        {
            db.Set<DeviceAuthorization>().Add(authorization);
            await db.SaveChangesAsync(ct);
            return true;
        }
        // 2601 and 2627: the unique user code index. Another live row holds the same user code.
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 }) { return false; }
        catch (DbUpdateException e) { throw new IdentitySecurityUnavailableException("Device sign-in could not be stored.", e); }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Device sign-in could not be stored.", e); }
        finally { db.ChangeTracker.Clear(); }
    }

    public async Task<DeviceAuthorization?> ReadDeviceAuthorizationAsync(Guid id, CancellationToken ct)
    {
        try { return await db.Set<DeviceAuthorization>().AsNoTracking().SingleOrDefaultAsync(x => x.ID == id, ct); }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Device sign-in unavailable.", e); }
    }

    public async Task<DeviceAuthorization?> FindDeviceAuthorizationAsync(byte[] userCodeDigest, CancellationToken ct)
    {
        try { return await db.Set<DeviceAuthorization>().AsNoTracking().SingleOrDefaultAsync(x => x.UserCodeDigest == userCodeDigest, ct); }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Device sign-in unavailable.", e); }
    }

    public async Task<DeviceAuthorization?> LockDeviceAuthorizationAsync(Guid id, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A device sign-in is locked for change only inside admission.");
        try { return await Hinted<DeviceAuthorization>("UPDLOCK, HOLDLOCK", nameof(DeviceAuthorization.ID), id).SingleOrDefaultAsync(ct); }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Device sign-in unavailable.", e); }
    }

    public async Task<T> AdmitDeviceAuthorizationAsync<T>(Guid id, Func<DeviceAuthorization?, T> transition, CancellationToken ct)
    {
        // No execution-strategy replay, as for admission: an uncertain commit must not repeat a poll's change.
        db.ChangeTracker.Clear();
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
            var row = await Hinted<DeviceAuthorization>("UPDLOCK, HOLDLOCK", nameof(DeviceAuthorization.ID), id).SingleOrDefaultAsync(ct);
            var result = transition(row);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateConcurrencyException e) { throw new IdentitySecurityConflictException("Concurrent device sign-in change.", e); }
        catch (DbUpdateException e) { throw new IdentitySecurityUnavailableException("Device sign-in change failed.", e); }
        catch (SqlException e) { throw new IdentitySecurityUnavailableException("Device sign-in unavailable.", e); }
        catch (IOException e) { throw new IdentitySecurityUnavailableException("Device sign-in commit outcome unavailable.", e); }
        finally { db.ChangeTracker.Clear(); }
    }

    /// <summary>
    /// Expires the device sign-ins whose deadline passed (their codes are cleared, a denied one keeps its state) and
    /// removes those that ended more than a day ago, in bounded batches. No account is involved, so no account is locked.
    /// The maintenance loop runs it only on a host that configured device sign-in.
    /// </summary>
    public async Task CleanupDeviceAuthorizationsAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        var due = await db.Set<DeviceAuthorization>().Where(x => x.ExpiresAt <= now && x.DeviceCodeDigest != null)
            .OrderBy(x => x.ExpiresAt).Select(x => x.ID).Take(100).ToListAsync(ct);
        if (due.Count > 0)
        {
            await db.Set<DeviceAuthorization>().Where(x => due.Contains(x.ID) && x.ExpiresAt <= now && x.State == Core.Authentication.DeviceAuthorizationState.Denied)
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.DeviceCodeDigest, (byte[]?)null).SetProperty(y => y.UserCodeDigest, (byte[]?)null), ct);
            await db.Set<DeviceAuthorization>().Where(x => due.Contains(x.ID) && x.ExpiresAt <= now &&
                    (x.State == Core.Authentication.DeviceAuthorizationState.Pending || x.State == Core.Authentication.DeviceAuthorizationState.Approved))
                .ExecuteUpdateAsync(x => x.SetProperty(y => y.State, Core.Authentication.DeviceAuthorizationState.Expired)
                    .SetProperty(y => y.DeviceCodeDigest, (byte[]?)null).SetProperty(y => y.UserCodeDigest, (byte[]?)null), ct);
        }
        var retentionCutoff = now.AddHours(-24);
        var old = await db.Set<DeviceAuthorization>().Where(x => x.ExpiresAt < retentionCutoff)
            .OrderBy(x => x.ExpiresAt).Select(x => x.ID).Take(100).ToListAsync(ct);
        if (old.Count > 0) await db.Set<DeviceAuthorization>().Where(x => old.Contains(x.ID)).ExecuteDeleteAsync(ct);
    }
}
