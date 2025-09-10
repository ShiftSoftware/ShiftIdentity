using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class ServiceRepository : ShiftRepository<ShiftIdentityDbContext, Service, ServiceListDTO, ServiceDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public ServiceRepository(ShiftIdentityDbContext db, ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override Task SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.ServiceFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Service Feature is locked"]));

        return base.SaveChangesAsync();
    }
}