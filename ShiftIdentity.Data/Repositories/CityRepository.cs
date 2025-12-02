using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CityRepository : ShiftRepository<ShiftIdentityDbContext, City, CityListDTO, CityDTO>
{
    private readonly RegionRepository regionRepo;
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;

    public CityRepository(
        ShiftIdentityDbContext db,
        RegionRepository regionRepo,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions,
        ShiftIdentityFeatureLocking shiftIdentityFeatureLocking,
        ShiftIdentityLocalizer Loc
    ) : base(db, x => x.IncludeRelatedEntitiesWithFindAsync(y => y.Include(z => z.Region).ThenInclude(z=> z.Country)))
    {
        this.regionRepo = regionRepo;
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override async ValueTask<City> UpsertAsync(City entity, CityDTO dto, ActionTypes actionType, long? userId, Guid? idempotencyKey, bool disableDefaultDataLevelAccess, bool disableGlobalFilters)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        var oldCountryId = entity.CountryID;

        entity.CountryID = (await this.regionRepo.FindAsync(dto.Region.Value.ToLong(), asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true))?.CountryID;

        if (actionType == ActionTypes.Update)
            if (entity.CountryID != oldCountryId && oldCountryId is not null)
                throw new ShiftEntityException(new Message(Loc["Error"], Loc["Country can not be changed after creation."]));

        return await base.UpsertAsync(entity, dto, actionType, userId, idempotencyKey, disableDefaultDataLevelAccess, disableGlobalFilters);
    }

    public override ValueTask<City> DeleteAsync(City entity, bool isHardDelete, long? userId, bool disableDefaultDataLevelAccess, bool disableGlobalFilters)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId, disableDefaultDataLevelAccess, disableGlobalFilters);
    }

    public override Task<int> SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.CityFeatureIsLocked)
            throw new ShiftEntityException(new Message("Error", Loc["City Feature is locked"]));

        return base.SaveChangesAsync();
    }
}