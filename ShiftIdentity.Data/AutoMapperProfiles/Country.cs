using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

// The entity↔DTO maps (Country↔CountryDTO, Country→CountryListDTO) are gone: the "api/IdentityCountry" endpoint
// is attribute-driven and uses the SOURCE-GENERATED mapper (see Country entity). Only the Cosmos REPLICATION map
// below remains — it has no generated equivalent and is still used by the replication pipeline.
public class Country : Profile
{
    public Country()
    {
        CreateMap<Data.Entities.Country, CountryModel>()
            .ForMember(x => x.CountryID, x => x.MapFrom(src => src.ID))
            .ForMember(x => x.ItemType, x => x.MapFrom(src => CountryContainerItemTypes.Country));
    }
}
