using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class DepartmentRepository : ShiftRepository<ShiftIdentityDbContext, Department, DepartmentListDTO, DepartmentDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    public DepartmentRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.DepartmentFeatureIsLocked)
            throw new ShiftEntityException(new Message("Error", "Department Feature is locked"));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}
