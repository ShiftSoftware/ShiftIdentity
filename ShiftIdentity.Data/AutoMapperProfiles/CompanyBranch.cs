
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core;
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
                    dest => dest.City,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.CityID.ToString()!, Text = src.City!.Name })
                )
            .ForMember(
                    dest => dest.Services,
                    opt => opt.MapFrom(src => src.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }))
                )
            .ForMember(
                    dest => dest.Departments,
                    opt => opt.MapFrom(src => src.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }))
                )
            .ForMember(
                    dest => dest.Brands,
                    opt => opt.MapFrom(src => src.CompanyBranchBrands.Select(y => new ShiftEntitySelectDTO { Value = y.BrandID.ToString()!, Text = y.Brand!.Name }))
                )
            .ForMember(
                dest => dest.CustomFields,
                opt => opt.MapFrom(src => src.CustomFields == null ? null : src.CustomFields
                .ToDictionary(x => x.Key, x =>
                new CustomFieldDTO
                {
                    DisplayName = x.Value.DisplayName,
                    IsPassword = x.Value.IsPassword,
                    IsEncrypted = x.Value.IsEncrypted,
                    Value = x.Value.IsPassword ? null : x.Value.Value,
                    HasValue = x.Value.Value != null
                }))
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
                    dest => dest.City,
                    opt => opt.MapFrom(src => src.City.Name)
                )
            .ForMember(
                    dest => dest.Brands,
                    opt => opt.MapFrom(src => src.CompanyBranchBrands!.Select(x => new ShiftEntitySelectDTO() { Value = x.BrandID.ToString(), Text = x.Brand!.Name }))
                )
            .ForMember(
                    dest => dest.Departments,
                    opt => opt.MapFrom(src => src.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }))
                )
            .ForMember(
                    dest => dest.Services,
                    opt => opt.MapFrom(src => src.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }))
                )
            .ForMember(
                    dest => dest.CountryDisplayOrder,
                    opt => opt.MapFrom(src => src.City != null && src.City.Region != null && src.City.Region.Country != null ? src.City.Region.Country.DisplayOrder : null)
                )
            .ForMember(
                    dest => dest.RegionDisplayOrder,
                    opt => opt.MapFrom(src => src.City != null && src.City.Region != null ? src.City.Region.DisplayOrder : null)
                )
            .ForMember(
                    dest => dest.CityDisplayOrder,
                    opt => opt.MapFrom(src => src.City != null ? src.City.DisplayOrder : null)
                )
            .ForMember(
                    dest => dest.CompanyDisplayOrder,
                    opt => opt.MapFrom(src => src.Company != null ? src.Company.DisplayOrder : null)
                );

        CreateMap<Core.Entities.CompanyBranch, CompanyBranchModel>()
            .ForMember(dest => dest.BranchID, opt => opt.MapFrom(src => src.ID))
            .ForMember(dest => dest.ItemType, opt => opt.MapFrom(src => CompanyBranchContainerItemTypes.Branch))
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.Location,
                opt => opt.MapFrom(src => (string.IsNullOrWhiteSpace(src.Longitude) || string.IsNullOrWhiteSpace(src.Latitude)) ? null : new Location(new decimal[] { decimal.Parse(src.Longitude), decimal.Parse(src.Latitude) }))
            );
    }
}
