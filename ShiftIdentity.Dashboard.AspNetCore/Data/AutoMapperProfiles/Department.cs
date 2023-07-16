
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class Department : Profile
{
    public Department()
    {
        CreateMap<ShiftIdentity.AspNetCore.Entities.Department, DepartmentDTO>();
        CreateMap<ShiftIdentity.AspNetCore.Entities.Department, DepartmentListDTO>();
    }
}
