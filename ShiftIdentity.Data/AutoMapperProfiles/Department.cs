﻿
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Department : Profile
{
    public Department()
    {
        CreateMap<Core.Entities.Department, DepartmentDTO>().ReverseMap();
        CreateMap<Core.Entities.Department, DepartmentListDTO>();
    }
}