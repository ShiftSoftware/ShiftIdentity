
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class Service : Profile
{
	public Service()
	{
        CreateMap<ShiftIdentity.AspNetCore.Entities.Service, ServiceDTO>();
        CreateMap<ShiftIdentity.AspNetCore.Entities.Service, ServiceListDTO>();
    }
}
