using AutoMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class ServiceRepository :
    ShiftRepository<ShiftIdentityDB, Service, ServiceListDTO, ServiceDTO, ServiceDTO>,
     IShiftRepositoryAsync<Service, ServiceListDTO, ServiceDTO>
{
    public ServiceRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Services, mapper)
    {
    }
}