﻿using AutoMapper;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class ServiceRepository : ShiftRepository<ShiftIdentityDbContext, Service, ServiceListDTO, ServiceDTO>
{
    public ServiceRepository(ShiftIdentityDbContext db) : base(db)
    {
    }
}