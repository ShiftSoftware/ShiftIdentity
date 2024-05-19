using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class BrandRepository : ShiftRepository<ShiftIdentityDbContext, Brand, BrandListDTO, BrandDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    public BrandRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.BrandFeatureIsLocked)
            throw new ShiftEntityException(new Message("Error", "Brand Feature is locked"));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}