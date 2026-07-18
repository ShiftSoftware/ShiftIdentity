using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;


namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (City↔CityDTO, City→CityListDTO) are gone: the "api/IdentityCity" endpoint is
// attribute-driven and uses the SOURCE-GENERATED mapper (see City entity, whose IConfiguresShiftRepository
// reproduces the Region→Country include + the flattened Region/Country/display-order list columns). Only the
// Cosmos REPLICATION maps below remain — they have no generated equivalent and drive the replication pipeline.
public class City : Profile
{
    public City()
    {
        CreateMap<Data.Entities.City, CityModel>()
            .ForMember(
                dest => dest.ItemType,
                opt => opt.MapFrom(src => CountryContainerItemTypes.City)
            )
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );

        CreateMap<Data.Entities.City, CityCompanyBranchModel>()
            .ForMember(
                dest => dest.id,
                opt => opt.MapFrom(src => src.ID.ToString())
            );
    }
}
