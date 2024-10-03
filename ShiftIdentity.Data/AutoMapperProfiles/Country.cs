using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Country;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Country : Profile
{
    public Country()
    {
        CreateMap<Core.Entities.Country, CountryDTO>().ReverseMap();
        CreateMap<Core.Entities.Country, CountryListDTO>();
    }
}
