using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;


namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class ServiceRepository :
    ShiftRepository<Service>,
     IShiftRepositoryAsync<Service, ServiceListDTO, ServiceDTO>
{
    private readonly ShiftIdentityDB db;
    public ServiceRepository(ShiftIdentityDB db) : base(db, db.Services)
    {
        this.db = db;
    }

    public ValueTask<Service> CreateAsync(ServiceDTO dto, long? userId = null)
    {
        var entity = new Service().CreateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Service>(entity);
    }

    public ValueTask<Service> DeleteAsync(Service entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<Service>(entity);
    }

    public async Task<Service> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false)
    {
        return await base.FindAsync(id, asOf, ignoreGlobalFilters: ignoreGlobalFilters);
    }

    public IQueryable<ServiceListDTO> OdataList(bool ignoreGlobalFilters = false)
    {
        var data = db.Services.AsNoTracking();

        if (ignoreGlobalFilters)
            data = data.IgnoreQueryFilters();

        return data.Select(x => (ServiceListDTO)x);
    }

    public ValueTask<Service> UpdateAsync(Service entity, ServiceDTO dto, long? userId = null)
    {
        entity.UpdateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Service>(entity);
    }

    public ValueTask<ServiceDTO> ViewAsync(Service entity)
    {
        return new ValueTask<ServiceDTO>(entity);
    }

    private void AssignValues(ServiceDTO dto, Service entity)
    {
        entity.Name = dto.Name;
    }
}