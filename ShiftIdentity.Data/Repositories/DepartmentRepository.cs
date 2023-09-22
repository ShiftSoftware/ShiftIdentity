using AutoMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class DepartmentRepository :
     ShiftRepository<ShiftIdentityDB, Department, DepartmentListDTO, DepartmentDTO, DepartmentDTO>,
     IShiftRepositoryAsync<Department, DepartmentListDTO, DepartmentDTO>
{
    public DepartmentRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Departments, mapper)
    {

    }
}
