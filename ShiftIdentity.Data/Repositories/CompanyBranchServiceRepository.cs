using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

internal class CompanyBranchServiceRepository : IShiftEntityPrepareForReplicationAsync<CompanyBranchService>
{
    private readonly ShiftIdentityDB db;

    public CompanyBranchServiceRepository(ShiftIdentityDB db)
    {
        this.db = db;
    }

    public async ValueTask<CompanyBranchService> PrepareForReplicationAsync(CompanyBranchService entity, ReplicationChangeType changeType)
    {
        var branch = await db.CompanyBranches.Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.ID == entity.CompanyBranchID);

        entity.CompanyBranch = branch;

        return entity;
    }
}
