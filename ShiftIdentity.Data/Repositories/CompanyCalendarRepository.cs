using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CompanyCalendarRepository : ShiftRepository<ShiftIdentityDbContext, CompanyCalendar, CompanyCalendarListDTO, CompanyCalendarDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;

    public CompanyCalendarRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityFeatureLocking shiftIdentityFeatureLocking)
        : base(db, x => x.IncludeRelatedEntitiesWithFindAsync(
            i => i.Include(e => e.Branches)))
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override Task<int> SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.CompanyCalendarFeatureIsLocked)
            throw new ShiftEntityException(
                new ShiftSoftware.ShiftEntity.Model.Message("Error", "Company Calendar feature is locked."));

        return base.SaveChangesAsync();
    }
}
