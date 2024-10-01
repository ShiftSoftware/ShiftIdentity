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
    public ServiceRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.ServiceFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Service Feature is locked"]));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}