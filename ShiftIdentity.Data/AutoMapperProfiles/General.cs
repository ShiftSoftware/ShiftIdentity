using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.ReplicationModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class General : Profile
{
    public General()
    {
        CreateMap<CompanyBranchService, CompanyBranchServiceModel>()
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => CompanyItemTypes.Service))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Service.Name))
            .ForMember(dest => dest.BranchID, opt => opt.MapFrom(src => src.CompanyBranchID))
            .ForMember(dest => dest.CompanyID, opt => opt.MapFrom(src => src.CompanyBranch.CompanyID))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ServiceID.ToString())
            );

        CreateMap<CompanyBranchDepartment, CompanyBranchDepartmentModel>()
            .ForMember(dest => dest.Type, opt => opt.MapFrom(src => CompanyItemTypes.Department))
            .ForMember(dest => dest.Name, opt => opt.MapFrom(src => src.Department.Name))
            .ForMember(dest => dest.BranchID, opt => opt.MapFrom(src => src.CompanyBranchID))
            .ForMember(dest => dest.CompanyID, opt => opt.MapFrom(src => src.CompanyBranch.CompanyID))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.DepartmentID.ToString())
            );
    }
}
