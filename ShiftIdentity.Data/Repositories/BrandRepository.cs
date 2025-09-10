using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class BrandRepository : ShiftRepository<ShiftIdentityDbContext, Brand, BrandListDTO, BrandDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public BrandRepository(ShiftIdentityDbContext db, ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override Task SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.BrandFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Brand Feature is locked"]));

        return base.SaveChangesAsync();
    }
}