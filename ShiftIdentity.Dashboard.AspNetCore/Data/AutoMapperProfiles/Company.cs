
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class Company : Profile
{
	public Company()
	{
        CreateMap<ShiftIdentity.AspNetCore.Entities.Company, CompanyDTO>();
        CreateMap<ShiftIdentity.AspNetCore.Entities.Company, CompanyListDTO>();
    }
}
