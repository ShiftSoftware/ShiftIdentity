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
                    opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.Region.ID.ToString()!, Text = src.Region!.Name })
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
                    opt => opt.MapFrom(src => src.Region.Name)
                );

        CreateMap<Core.Entities.City, CityModel>()
            .ForMember(
                dest => dest.CountryID,
                opt => opt.MapFrom(src => (src.Region != null? src.Region.CountryID.ToString(): null))
            )
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.RegionID.ToString())
            )
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
