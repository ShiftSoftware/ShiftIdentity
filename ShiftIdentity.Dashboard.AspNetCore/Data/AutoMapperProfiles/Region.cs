﻿
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.AutoMapperProfiles;

public class Region : Profile
{
	public Region()
	{
        CreateMap<Core.Entities.Region, RegionDTO>().ReverseMap();
        CreateMap<Core.Entities.Region, RegionListDTO>();
    }
}
