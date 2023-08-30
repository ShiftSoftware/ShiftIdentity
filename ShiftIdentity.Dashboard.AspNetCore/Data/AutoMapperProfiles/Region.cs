
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class Region : Profile
{
	public Region()
	{
        CreateMap<ShiftIdentity.AspNetCore.Entities.Region, RegionDTO>().ReverseMap();
        CreateMap<ShiftIdentity.AspNetCore.Entities.Region, RegionListDTO>();
    }
}
