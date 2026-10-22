using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// Prepares User for Cosmos replication (auto-registered by RegisterIShiftEntityPrepareForReplication).
// UserModel.AccessTreeIDs lists the assigned named trees (User.AccessTrees). Load them by query on the same context,
// because many saves of a user never load the navigation: a sign-in, a password change or a lockout saves the user
// row too. The tracked navigation would then be empty or out of date, and the document would claim it is complete.
//
// UserRepository is also registered as this preparer (every repository is), and the save trigger gets whichever
// registration comes last. UserRepository runs this class too, so the order does not matter.
internal class UserReplicationPreparer : IShiftEntityPrepareForReplicationAsync<User>
{
    private readonly ShiftIdentityDbContext db;

    public UserReplicationPreparer(ShiftIdentityDbContext db)
    {
        this.db = db;
    }

    public async ValueTask<User> PrepareForReplicationAsync(User entity, ReplicationChangeType changeType)
    {
        entity.AccessTrees = await db.Set<UserAccessTree>()
            .Where(x => x.UserID == entity.ID)
            .ToListAsync();

        return entity;
    }
}
