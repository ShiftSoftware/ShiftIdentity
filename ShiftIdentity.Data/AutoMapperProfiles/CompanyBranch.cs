
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class CompanyBranch : Profile
{
    public CompanyBranch()
    {
        CreateMap<Core.Entities.CompanyBranch, CompanyBranchDTO>()
            .ForMember(
                    dest => dest.Company,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.CompanyID.ToString()!, Text = src.Company!.Name })
                )
            .ForMember(
                    dest => dest.Region,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.RegionID.ToString()!, Text = src.Region!.Name })
                )
            .ForMember(
                    dest => dest.Services,
                    opt => opt.MapFrom(src => src.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }))
                )
            .ForMember(
                    dest => dest.Departments,
                    opt => opt.MapFrom(src => src.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }))
                );


        CreateMap<Core.Entities.CompanyBranch, CompanyBranchListDTO>()
            .ForMember(
                    dest => dest.Company,
                    opt => opt.MapFrom(src => src.Company.Name)
                )
            .ForMember(
                    dest => dest.Region,
                    opt => opt.MapFrom(src => src.Region.Name)
                )
            .ForMember(
                    dest => dest.Departments,
                    opt => opt.MapFrom(src => src.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }))
                );
    }
}
