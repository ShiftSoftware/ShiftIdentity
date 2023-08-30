
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class Company : Profile
{
	public Company()
	{
        CreateMap<ShiftIdentity.AspNetCore.Entities.Company, CompanyDTO>();

        CreateMap<CompanyDTO, ShiftIdentity.AspNetCore.Entities.Company>().ForMember(
            m => m.HQPhone,
            opt => opt.MapFrom(x => x.HQPhone == null ? null : Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(x.HQPhone))
        );

        CreateMap<ShiftIdentity.AspNetCore.Entities.Company, CompanyListDTO>();
    }
}
