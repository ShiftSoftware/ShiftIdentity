using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

internal class CompanyBranchServiceRepository : IShiftEntityPrepareForReplicationAsync<CompanyBranchService>
{
    private readonly ShiftIdentityDbContext db;

    public CompanyBranchServiceRepository(ShiftIdentityDbContext db)
    {
        this.db = db;
    }

    public async ValueTask<CompanyBranchService> PrepareForReplicationAsync(CompanyBranchService entity, ReplicationChangeType changeType)
    {
        // The replicated CompanyBranchSubItemModel takes Name/IntegrationId from the Service nav, which is null on the
        // freshly-saved join (the sync sets only the FK) and isn't lazy-loaded — load it here (one query) so the
        // sub-item carries real values instead of nulls.
        entity.Service = await db.Services.SingleOrDefaultAsync(x => x.ID == entity.ServiceID);

        return entity;
    }
}
