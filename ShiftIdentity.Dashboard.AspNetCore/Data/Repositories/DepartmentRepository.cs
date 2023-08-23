using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class DepartmentRepository :
     ShiftRepository<ShiftIdentityDB, Department>,
     IShiftRepositoryAsync<Department, DepartmentListDTO, DepartmentDTO>
{
    private readonly ShiftIdentityDB db;

    public DepartmentRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Departments, mapper)
    {
        this.db = db;
    }

    public ValueTask<Department> CreateAsync(DepartmentDTO dto, long? userId = null)
    {
        var entity = new Department().CreateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Department>(entity);
    }

    public ValueTask<Department> DeleteAsync(Department entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<Department>(entity);
    }

    public async Task<Department> FindAsync(long id, DateTime? asOf = null)
    {
        return await base.FindAsync(id, asOf);
    }

    public IQueryable<DepartmentListDTO> OdataList(bool showDeletedRows = false)
    {
        var data = GetIQueryable(showDeletedRows).AsNoTracking();

        return mapper.ProjectTo<DepartmentListDTO>(data);
    }

    public ValueTask<Department> UpdateAsync(Department entity, DepartmentDTO dto, long? userId = null)
    {
        entity.UpdateShiftEntity(userId);

        AssignValues(dto, entity);

        return new ValueTask<Department>(entity);
    }

    public ValueTask<DepartmentDTO> ViewAsync(Department entity)
    {
        return new ValueTask<DepartmentDTO>(mapper.Map<DepartmentDTO>(entity));
    }

    private void AssignValues(DepartmentDTO dto, Department entity)
    {
        entity.Name = dto.Name;
    }
}
