using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CityRepository : ShiftRepository<ShiftIdentityDbContext, City, CityListDTO, CityDTO>
{
    public CityRepository(ShiftIdentityDbContext db) : base(db, x => x.IncludeRelatedEntitiesWithFindAsync(y => y.Include(z => z.Region))) { }

    public override ValueTask<City> UpsertAsync(City entity, CityDTO dto, ActionTypes actionType, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        return base.UpsertAsync(entity, dto, actionType, userId);
    }

    public override ValueTask<City> DeleteAsync(City entity, bool isHardDelete = false, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }
}