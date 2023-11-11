using AutoMapper;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class DepartmentRepository : ShiftRepository<ShiftIdentityDbContext, Department, DepartmentListDTO, DepartmentDTO>
{
    public DepartmentRepository(ShiftIdentityDbContext db, IMapper mapper) : base(db, mapper)
    {

    }
}
