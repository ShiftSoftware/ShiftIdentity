using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class DepartmentRepository : ShiftRepository<ShiftIdentityDbContext, Department, DepartmentListDTO, DepartmentDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public DepartmentRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.DepartmentFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Department Feature is locked"]));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}
