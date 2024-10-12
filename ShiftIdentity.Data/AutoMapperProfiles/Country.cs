using AutoMapper;
using ShiftSoftware.ShiftEntity.Model.Replication.IdentityModels;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Country : Profile
{
    public Country()
    {
        CreateMap<Core.Entities.Country, CountryDTO>().ReverseMap();
        CreateMap<Core.Entities.Country, CountryListDTO>();
        CreateMap<Core.Entities.Country, CountryModel>()
            .ForMember(x => x.CountryID, x => x.MapFrom(src => src.ID))
            .ForMember(x => x.ItemType, x => x.MapFrom(src => CountryContainerItemTypes.Country));
    }
}
