using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CountryRepository : ShiftRepository<ShiftIdentityDbContext, Country, CountryListDTO, CountryDTO>
{
    private readonly ShiftIdentityLocalizer localizer;
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;

    public CountryRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityLocalizer localizer,
         ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.localizer = localizer;
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override ValueTask<Country> UpsertAsync(Country entity, CountryDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(localizer["Error"], localizer["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.UpsertAsync(entity, dto, actionType, userId, idempotencyKey);
    }

    public override ValueTask<Country> DeleteAsync(Country entity, bool isHardDelete = false, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(localizer["Error"], localizer["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.CountryFeatureIsLocked)
            throw new ShiftEntityException(new Message(localizer["Error"], localizer["Region Feature is locked"]));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}
