using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Service;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class ServiceRepository : ShiftRepository<ShiftIdentityDbContext, Service, ServiceListDTO, ServiceDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    public ServiceRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.ServiceFeatureIsLocked)
            throw new ShiftEntityException(new Message("Error", "Service Feature is locked"));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}