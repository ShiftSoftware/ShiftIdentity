using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public sealed record LegacyTotpMigrationBatch(long LastUserID, int Examined, int Copied, int Cleared);

public sealed partial class SqlIdentitySecurityStore
{
    /// <summary>
    /// Visits a bounded batch of the users whose legacy factor still needs a protected copy: a plaintext factor, a security
    /// row without a protected factor and no local MFA recovery. Inactive and deleted users are included; users with nothing
    /// to copy are not visited. The caller supplies encryption and verification. Each row commits independently, so an
    /// interrupted startup can resume without replacing a protected factor, and the locked re-read decides again, so a factor
    /// protected in the meantime is only verified.
    /// <para>
    /// An empty legacy factor (a column that is not NULL but has no bytes) is not a factor, so there is nothing to copy. It is
    /// visited whatever its security row holds, set to NULL under the same lock, and counted in <see cref="LegacyTotpMigrationBatch.Cleared"/>.
    /// The caller is not called for it, and a later start does not visit that user again. Releases 2026.9.21.1 to 2026.10.7.2
    /// wrote such values when a user was created through the dashboard.
    /// </para>
    /// </summary>
    public async Task<LegacyTotpMigrationBatch> MigrateLegacyTotpBatchAsync(long afterUserID, int batchSize,
        Action<UserSecurityState, byte[]?> migrate, CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var ids = await db.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.ID > afterUserID && x.TotpSecret != null && (x.TotpSecret.Length == 0 || db.Set<UserSecurityState>()
                .Any(s => s.UserID == x.ID && s.ProtectedTotpSecret == null && !s.LocalMfaRecoveryRequired)))
            .OrderBy(x => x.ID).Select(x => x.ID).Take(batchSize).ToListAsync(ct);
        var copied = 0;
        var cleared = 0;
        foreach (var id in ids)
        {
            var outcome = await AdmitUsersAsync([id], id, null, new("", ""), async units =>
            {
                // Security state is already locked. Keep the legacy user row locked while copying as well.
                // Read afresh in case a legacy writer changed the row before this lock.
                var user = await Hinted<User>("UPDLOCK, HOLDLOCK", nameof(User.ID), id)
                    .IgnoreQueryFilters().AsNoTracking().SingleAsync(ct);
                if (user.TotpSecret is { Length: 0 })
                {
                    // Not a factor. Clear it in this transaction, under the lock taken above. A direct update writes only
                    // this column and does not go through the save pipeline.
                    await db.Users.IgnoreQueryFilters().Where(x => x.ID == id)
                        .ExecuteUpdateAsync(x => x.SetProperty(u => u.TotpSecret, (byte[]?)null), ct);
                    return (Copied: 0, Cleared: 1);
                }
                var state = units[id].Security;
                var hadCopy = state.ProtectedTotpSecret is not null;
                migrate(state, user.TotpSecret);
                return (Copied: !hadCopy && state.ProtectedTotpSecret is not null ? 1 : 0, Cleared: 0);
            }, ct, requireClient: false);
            copied += outcome.Copied;
            cleared += outcome.Cleared;
        }
        return new(ids.Count == 0 ? afterUserID : ids[^1], ids.Count, copied, cleared);
    }

    /// <summary>True when any user, inactive and deleted users included, has no security row.</summary>
    public Task<bool> AnyUserWithoutSecurityStateAsync(CancellationToken ct = default) =>
        db.Users.IgnoreQueryFilters().AnyAsync(x => !db.Set<UserSecurityState>().Any(s => s.UserID == x.ID), ct);

    /// <summary>
    /// Reads a bounded page of protected factors in user order, without locks, for a decryption check. Each row's ciphertext
    /// and the values its protection is bound to come from one committed version of that row.
    /// </summary>
    public Task<List<UserSecurityState>> ReadProtectedFactorsAsync(long afterUserID, int batchSize, CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        return db.Set<UserSecurityState>().AsNoTracking()
            .Where(x => x.UserID > afterUserID && x.ProtectedTotpSecret != null).OrderBy(x => x.UserID).Take(batchSize)
            .Select(x => new UserSecurityState
            {
                UserID = x.UserID, FactorGeneration = x.FactorGeneration,
                TotpProtectionVersion = x.TotpProtectionVersion, ProtectedTotpSecret = x.ProtectedTotpSecret
            })
            .ToListAsync(ct);
    }
}
