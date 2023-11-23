using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class General : Profile
{
    public General()
    {
        CreateMap<CompanyBranchService, CompanyBranchServiceModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Service))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Service.Name))
            .ForMember(dest => dest.BranchID, opt => opt.MapFrom(src => src.CompanyBranchID))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ServiceID.ToString())
            )
            .ForMember(
                dest => dest.ID,
                opt => opt.MapFrom(src => src.ServiceID.ToString())
            );

        CreateMap<CompanyBranchDepartment, CompanyBranchDepartmentModel>()
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Department))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Department.Name))
            .ForMember(dest => dest.BranchID, opt => opt.MapFrom(src => src.CompanyBranchID))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.DepartmentID.ToString())
            )
            .ForMember(
                dest => dest.ID,
                opt => opt.MapFrom(src => src.DepartmentID.ToString())
            );
    }
}
