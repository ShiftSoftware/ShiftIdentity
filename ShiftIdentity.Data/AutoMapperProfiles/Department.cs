
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Department : Profile
{
    public Department()
    {
        CreateMap<Core.Entities.Department, DepartmentDTO>().ReverseMap();
        CreateMap<Core.Entities.Department, DepartmentListDTO>();
        CreateMap<Core.Entities.Department, DepartmentModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
        CreateMap<Core.Entities.Department, CompanyBranchDepartmentModel>()
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => CompanyItemTypes.Department))
            .ForMember(dest => dest.BranchID, opt => opt.Ignore())
            .ForMember(dest => dest.CompanyID, opt => opt.Ignore())
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID)
            );
    }
}
