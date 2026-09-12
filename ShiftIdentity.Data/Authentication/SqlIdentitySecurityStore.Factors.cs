using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Authentication;

public sealed record LegacyTotpMigrationBatch(long LastUserID, int Examined, int Copied);

public sealed partial class SqlIdentitySecurityStore
{
    /// <summary>
    /// Visits a bounded batch, including inactive and deleted users. The caller supplies encryption and verification.
    /// Each row commits independently, so an interrupted startup can resume without replacing a protected factor.
    /// </summary>
    public async Task<LegacyTotpMigrationBatch> MigrateLegacyTotpBatchAsync(long afterUserID, int batchSize,
        Action<UserSecurityState, byte[]?> migrate, CancellationToken ct = default)
    {
        if (batchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(batchSize));
        var ids = await db.Users.IgnoreQueryFilters().AsNoTracking().Where(x => x.ID > afterUserID)
            .OrderBy(x => x.ID).Select(x => x.ID).Take(batchSize).ToListAsync(ct);
        var copied = 0;
        foreach (var id in ids)
        {
            copied += await AdmitUsersAsync([id], id, null, new("", ""), async units =>
            {
                // Security state is already locked. Keep the legacy user row locked while copying as well.
                // Read afresh in case a legacy writer changed the row before this lock.
                var user = await Hinted<User>("UPDLOCK, HOLDLOCK", nameof(User.ID), id)
                    .IgnoreQueryFilters().AsNoTracking().SingleAsync(ct);
                var state = units[id].Security;
                var hadCopy = state.ProtectedTotpSecret is not null;
                migrate(state, user.TotpSecret);
                return !hadCopy && state.ProtectedTotpSecret is not null ? 1 : 0;
            }, ct, requireClient: false);
        }
        return new(ids.Count == 0 ? afterUserID : ids[^1], ids.Count, copied);
    }
}
