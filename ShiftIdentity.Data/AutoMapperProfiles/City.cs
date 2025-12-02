using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;


namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class City : Profile
{
    public City()
    {
        CreateMap<Core.Entities.City, CityDTO>()
            .ForMember(
                    dest => dest.Region,
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.RegionID.ToString()!, Text = src.Region!.Name })
                )
            .ReverseMap()
            .ForMember(x => x.Region, x => x.Ignore())
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.Region.Value.ToLong())
            );

        CreateMap<Core.Entities.City, CityListDTO>()
            .ForMember(
                    dest => dest.Region,
                    opt => opt.MapFrom(src => src.Region!.Name)
                )
            .ForMember(
                    dest => dest.Country,
                    opt => opt.MapFrom(src => src.Region!.Country!.Name)
                )
            .ForMember(
                    dest => dest.CountryDisplayOrder,
                    opt => opt.MapFrom(src => src.Region != null && src.Region.Country != null ? src.Region.Country.DisplayOrder : null)
                )
            .ForMember(
                    dest => dest.RegionDisplayOrder,
                    opt => opt.MapFrom(src => src.Region != null ? src.Region.DisplayOrder : null)
                );

        CreateMap<Core.Entities.City, CityModel>()
            .ForMember(
                dest => dest.ItemType,
                opt => opt.MapFrom(src => CountryContainerItemTypes.City)
            )
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );

        CreateMap<Core.Entities.City, CityCompanyBranchModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
