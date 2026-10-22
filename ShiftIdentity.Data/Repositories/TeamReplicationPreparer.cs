using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// Prepares Team for Cosmos replication (auto-registered by RegisterIShiftEntityPrepareForReplication).
// TeamModel embeds its branches (TeamModel.CompanyBranches, built from Team.TeamCompanyBranches); those joins are
// saved with only their FK, so load them WITH their CompanyBranch nav in ONE query — otherwise each embedded sub-item
// replicates with null Name/IntegrationId.
// TeamModel.UserIDs lists the members (Team.TeamUsers). They are loaded by query too, because the tracked navigation
// is only as complete as the code that saved the team made it. A list that misses a member, or keeps a removed one,
// would be written to the document as if it were complete.
internal class TeamReplicationPreparer : IShiftEntityPrepareForReplicationAsync<Team>
{
    private readonly ShiftIdentityDbContext db;

    public TeamReplicationPreparer(ShiftIdentityDbContext db)
    {
        this.db = db;
    }

    public async ValueTask<Team> PrepareForReplicationAsync(Team entity, ReplicationChangeType changeType)
    {
        entity.TeamCompanyBranches = await db.Set<TeamCompanyBranch>()
            .Include(x => x.CompanyBranch)
            .Where(x => x.TeamID == entity.ID)
            .ToListAsync();

        entity.TeamUsers = await db.Set<TeamUser>()
            .Where(x => x.TeamID == entity.ID)
            .ToListAsync();

        return entity;
    }
}
