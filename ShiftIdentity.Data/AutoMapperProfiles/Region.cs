
using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (Region↔RegionDTO, Region→RegionListDTO) are gone: the "api/IdentityRegion" endpoint is
// attribute-driven and uses the SOURCE-GENERATED mapper (see Region entity, whose IConfiguresShiftRepository
// reproduces the Country include + the flattened Country/CountryDisplayOrder list columns). Only the Cosmos
// REPLICATION maps below remain — they have no generated equivalent and are still used by the replication pipeline.
public class Region : Profile
{
    public Region()
    {
        CreateMap<Data.Entities.Region, RegionModel>()
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

        CreateMap<Data.Entities.Region, CityRegionModel>()
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
