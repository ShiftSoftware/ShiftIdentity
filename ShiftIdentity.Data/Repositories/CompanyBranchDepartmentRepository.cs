using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

internal class CompanyBranchDepartmentRepository : IShiftEntityPrepareForReplicationAsync<CompanyBranchDepartment>
{
    private readonly ShiftIdentityDbContext db;

    public CompanyBranchDepartmentRepository(ShiftIdentityDbContext db)
    {
        this.db = db;
    }

    public async ValueTask<CompanyBranchDepartment> PrepareForReplicationAsync(CompanyBranchDepartment entity, ReplicationChangeType changeType)
    {
        // Load the Department nav (null on the freshly-saved join, not lazy-loaded) so the replicated
        // CompanyBranchSubItemModel carries the real Name/IntegrationId instead of nulls. One query.
        entity.Department = await db.Departments.SingleOrDefaultAsync(x => x.ID == entity.DepartmentID);

        return entity;
    }
}
