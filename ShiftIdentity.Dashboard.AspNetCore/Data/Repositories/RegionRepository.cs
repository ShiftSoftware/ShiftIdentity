using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;


namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class RegionRepository :
    ShiftRepository<Region>,
     IShiftRepositoryAsync<Region, RegionListDTO, RegionDTO>
{
    private readonly ShiftIdentityDB db;
    public RegionRepository(ShiftIdentityDB db) : base(db, db.Regions)
    {
        this.db = db;
    }

    public ValueTask<Region> CreateAsync(RegionDTO dto, long? userId = null)
    {
        var entity = new Region().CreateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Region>(entity);
    }

    public ValueTask<Region> DeleteAsync(Region entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<Region>(entity);
    }

    public async Task<Region> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false)
    {
        return await base.FindAsync(id, asOf, ignoreGlobalFilters: ignoreGlobalFilters);
    }

    public IQueryable<RegionListDTO> OdataList(bool ignoreGlobalFilters = false)
    {
        var data = db.Regions.AsNoTracking();

        if (ignoreGlobalFilters)
            data = data.IgnoreQueryFilters();

        return data.Select(x => (RegionListDTO)x);
    }

    public ValueTask<Region> UpdateAsync(Region entity, RegionDTO dto, long? userId = null)
    {
        entity.UpdateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Region>(entity);
    }

    public ValueTask<RegionDTO> ViewAsync(Region entity)
    {
        return new ValueTask<RegionDTO>(entity);
    }

    private void AssignValues(RegionDTO dto, Region entity)
    {
        entity.Name = dto.Name;
        entity.ExternalId = dto.ExternalId;
        entity.ShortCode = dto.ShortCode;
    }
}
