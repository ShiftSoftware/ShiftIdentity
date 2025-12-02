using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CountryRepository : ShiftRepository<ShiftIdentityDbContext, Country, CountryListDTO, CountryDTO>
{
    private readonly ShiftIdentityLocalizer localizer;
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;

    public CountryRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityLocalizer localizer,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions,
         ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.localizer = localizer;
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override ValueTask<Country> UpsertAsync(Country entity, CountryDTO dto, ActionTypes actionType, long? userId, Guid? idempotencyKey, bool disableDefaultDataLevelAccess, bool disableGlobalFilters)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(localizer["Error"], localizer["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.UpsertAsync(entity, dto, actionType, userId, idempotencyKey, disableDefaultDataLevelAccess, disableGlobalFilters);
    }

    public override ValueTask<Country> DeleteAsync(Country entity, bool isHardDelete, long? userId, bool disableDefaultDataLevelAccess, bool disableGlobalFilters)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(localizer["Error"], localizer["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId, disableDefaultDataLevelAccess, disableGlobalFilters);
    }

    public override Task<int> SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.CountryFeatureIsLocked)
            throw new ShiftEntityException(new Message(localizer["Error"], localizer["Country Feature is locked"]));

        return base.SaveChangesAsync();
    }
}
