using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class RegionRepository : ShiftRepository<ShiftIdentityDbContext, Region, RegionListDTO, RegionDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public RegionRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions,
        ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, 
        ShiftIdentityLocalizer Loc) : base(db, x=> x.IncludeRelatedEntitiesWithFindAsync(i=> i.Include(s=> s.Country)))
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override ValueTask<Region> UpsertAsync(Region entity, RegionDTO dto, ActionTypes actionType, long? userId, Guid? idempotencyKey, bool disableDefaultDataLevelAccess, bool disableGlobalFilters)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.UpsertAsync(entity, dto, actionType, userId, idempotencyKey, disableDefaultDataLevelAccess, disableGlobalFilters);
    }

    public override ValueTask<Region> DeleteAsync(Region entity, bool isHardDelete, long? userId, bool disableDefaultDataLevelAccess, bool disableGlobalFilters)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId, disableDefaultDataLevelAccess, disableGlobalFilters);
    }

    public override Task<int> SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.RegionFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Region Feature is locked"]));

        return base.SaveChangesAsync();
    }
}
