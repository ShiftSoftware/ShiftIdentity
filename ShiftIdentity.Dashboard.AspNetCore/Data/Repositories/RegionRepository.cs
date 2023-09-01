using AutoMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class RegionRepository :
    ShiftRepository<ShiftIdentityDB, Region, RegionListDTO, RegionDTO, RegionDTO>,
     IShiftRepositoryAsync<Region, RegionListDTO, RegionDTO>
{
    public RegionRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Regions, mapper)
    {
    }
}
