using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// Prepares CompanyBranchBrand for Cosmos replication (auto-registered by RegisterIShiftEntityPrepareForReplication).
// Was MISSING — unlike Service/Department, the Brand join had no preparer, so its branch sub-items replicated with
// null Name/IntegrationId.
internal class CompanyBranchBrandRepository : IShiftEntityPrepareForReplicationAsync<CompanyBranchBrand>
{
    private readonly ShiftIdentityDbContext db;

    public CompanyBranchBrandRepository(ShiftIdentityDbContext db)
    {
        this.db = db;
    }

    public async ValueTask<CompanyBranchBrand> PrepareForReplicationAsync(CompanyBranchBrand entity, ReplicationChangeType changeType)
    {
        // Load the Brand nav (null on the freshly-saved join, not lazy-loaded) so the replicated
        // CompanyBranchSubItemModel carries the real Name/IntegrationId instead of nulls. One query.
        entity.Brand = await db.Brands.SingleOrDefaultAsync(x => x.ID == entity.BrandID);

        return entity;
    }
}
