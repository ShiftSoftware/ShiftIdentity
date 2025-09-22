
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Region : Profile
{
    public Region()
    {
        CreateMap<Core.Entities.Region, RegionDTO>()
            .ForMember(
                dest => dest.Country,
                opt => opt.MapFrom(src => new ShiftEntitySelectDTO { Value = src.CountryID.ToString()!, Text = src.Country!.Name })
            )
            .ReverseMap()
            .ForMember(
                dest => dest.CountryID,
                opt => opt.MapFrom(src => src.Country.Value.ToLong())
            );

        CreateMap<Core.Entities.Region, RegionListDTO>()
            .ForMember(x => x.Country, x => x.MapFrom(src => src.Country != null ? src.Country.Name : null));

        CreateMap<Core.Entities.Region, RegionModel>()
            .ForMember(
                dest => dest.CountryID,
                opt => opt.MapFrom(src => src.CountryID)
            )
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.ItemType,
                opt => opt.MapFrom(src => CountryContainerItemTypes.Region)
            )
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );

        CreateMap<Core.Entities.Region, CityRegionModel>()
            .ForMember(
                dest => dest.CountryID,
                opt => opt.MapFrom(src => src.CountryID)
            )
            .ForMember(
                dest => dest.RegionID,
                opt => opt.MapFrom(src => src.ID.ToString())
            )
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
