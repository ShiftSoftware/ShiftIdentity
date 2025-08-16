using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly IConfiguration configuration;
    public CompanyRepository(ShiftIdentityDbContext db, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc, IConfiguration configuration) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.configuration = configuration;
    }

    public override ValueTask<Company> UpsertAsync(Company entity, CompanyDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        if (!string.IsNullOrWhiteSpace(dto.HQPhone))
        {
            if (!configuration.GetSection("ShiftIdentity").Exists() || configuration.GetSection("ShiftIdentity:DisablePhoneNumberValidation").Value == "False")
            {
                if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.HQPhone))
                    throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]));

                dto.HQPhone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.HQPhone);
            }
            else
            {
                dto.HQPhone = dto.HQPhone;
            }
        }
        else
            entity.HQPhone = null;

        var parentCompanyId = String.IsNullOrWhiteSpace(dto.ParentCompany?.Value)? null: dto.ParentCompany?.Value?.ToLong();

        if (actionType == ActionTypes.Update && parentCompanyId != null)
        {
            // Prevent self-reference
            if (parentCompanyId == entity.ID)
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Parent company and the child company should not be the same"]));

            // Prevent circular reference
            var parentId = parentCompanyId;
            do
            {
                var parent = db.Companies
                    .AsNoTracking()
                    .FirstOrDefault(x => x.ID == parentId);

                if (parent == null)
                    break;

                if (parent.ID == entity.ID)
                    throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Circular reference is not allowed"]));

                parentId = parent.ParentCompanyID;
            } while (parentCompanyId != null);
        }

        var result = base.UpsertAsync(entity, dto, actionType, userId);

        entity.ParentCompanyID = parentCompanyId;

        return result;
    }

    public override ValueTask<Company> DeleteAsync(Company entity, bool isHardDelete = false, long? userId = null, bool disableDefaultDataLevelAccess = false)
    {
        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

        return base.DeleteAsync(entity, isHardDelete, userId, disableDefaultDataLevelAccess);
    }

    public override Task SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.CompanyFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Company Feature is locked"]));

        return base.SaveChangesAsync();
    }
}
