using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class BrandRepository : ShiftRepository<ShiftIdentityDbContext, Brand, BrandListDTO, BrandDTO>
{
    public BrandRepository(ShiftIdentityDbContext db) : base(db)
    {
    }
}
