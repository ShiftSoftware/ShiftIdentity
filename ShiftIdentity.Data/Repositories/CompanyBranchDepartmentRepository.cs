using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

internal class CompanyBranchDepartmentRepository : IShiftEntityPrepareForReplicationAsync<CompanyBranchDepartment>
{
    private readonly ShiftIdentityDB db;

    public CompanyBranchDepartmentRepository(ShiftIdentityDB db)
    {
        this.db = db;
    }

    public async ValueTask<CompanyBranchDepartment> PrepareForReplicationAsync(CompanyBranchDepartment entity, ReplicationChangeType changeType)
    {
        var branch = await db.CompanyBranches.Include(x => x.Company)
            .SingleOrDefaultAsync(x => x.ID == entity.CompanyBranchID);

        entity.CompanyBranch = branch;

        return entity;
    }
}
