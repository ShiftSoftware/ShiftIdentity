using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class CompanyRepository : ShiftRepository<ShiftIdentityDbContext, Company, CompanyListDTO, CompanyDTO>
{
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public CompanyRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
    }

    public override ValueTask<Company> UpsertAsync(Company entity, CompanyDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        if (!string.IsNullOrWhiteSpace(dto.HQPhone))
        {
            if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.HQPhone))
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]));
        }

        return base.UpsertAsync(entity, dto, actionType, userId);
    }

    public override ValueTask<Company> DeleteAsync(Company entity, bool isHardDelete = false, long? userId = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId);
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.CompanyFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Company Feature is locked"]));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}
