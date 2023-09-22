﻿
using AutoMapper;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;

namespace ShiftSoftware.ShiftIdentity.Data.AutoMapperProfiles;

public class Service : Profile
{
    public Service()
    {
        CreateMap<Core.Entities.Service, ServiceDTO>().ReverseMap();
        CreateMap<Core.Entities.Service, ServiceListDTO>();
    }
}