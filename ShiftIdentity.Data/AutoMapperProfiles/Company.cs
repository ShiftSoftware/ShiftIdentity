
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Company : Profile
{
    public Company()
    {
        CreateMap<Core.Entities.Company, CompanyDTO>();

        CreateMap<CompanyDTO, Core.Entities.Company>().ForMember(
            m => m.HQPhone,
            opt => opt.MapFrom(x => x.HQPhone == null ? null : Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(x.HQPhone))
        );

        CreateMap<Core.Entities.Company, CompanyListDTO>();
    }
}
