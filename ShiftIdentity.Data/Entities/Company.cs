using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung C): Company's CRUD routes through the THIN CompanyRepository (kept only for
// ApplyPostODataProcessing) — hence the custom-repository attribute variant (no UseGeneratedMapper flag; the repo
// opts into the generated mapper in its own builder). Write logic (phone/circular-ref/CustomFields merge) lives
// here via IUpsertsShiftRepository, which fires through the non-overridden built-in upsert even with a custom repo.
// The protected-row guard + feature lock are central.
[TemporalShiftEntity]
[Table("Companies", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<CompanyListDTO, CompanyDTO, ShiftIdentityActions, CompanyRepository>("api/IdentityCompany", nameof(ShiftIdentityActions.Companies))]
public class Company : ShiftEntity<Company>, IEntityHasCompany<Company>, IShiftEntityReplication, IShiftEntityProtectable,
    IUpsertsShiftRepository<Company, CompanyListDTO, CompanyDTO>
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? LegalName { get; set; }
    public string? IntegrationId { get; set; }
    public string? ShortCode { get; set; }
    public CompanyTypes CompanyType { get; set; }
    public string? Logo { get; set; }
    public string? HQPhone { get; set; }
    public string? HQEmail { get; set; }
    public string? HQAddress { get; set; }
    public string? Website { get; set; }
    public bool IsProtected { get; set; }
    public DateTime? TerminationDate { get; set; }
    public Dictionary<string, CustomField>? CustomFields { get; set; }


    public long? ParentCompanyID { get; set; }
    public Company ParentCompany { get; set; }
    public virtual ICollection<Company> ChildCompanies { get; set; } = new HashSet<Company>();

    public virtual ICollection<CompanyBranch> CompanyBranches { get; set; }
    public long? CompanyID { get; set; }

    public int? DisplayOrder { get; set; }

    public Company()
    {
        CompanyBranches = new HashSet<CompanyBranch>();
        CustomFields = new();
    }

    public async ValueTask<Company> UpsertAsync(
        Company entity,
        CompanyDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<Company, CompanyListDTO, CompanyDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();
        var Loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();
        var configuration = context.Services.GetRequiredService<IConfiguration>();

        // Phone validate/format — BEFORE Base() so MapToEntity maps the formatted value.
        if (!string.IsNullOrWhiteSpace(dto.HQPhone))
        {
            if (!configuration.GetSection("ShiftIdentity").Exists() || configuration.GetSection("ShiftIdentity:DisablePhoneNumberValidation").Value == "False")
            {
                if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.HQPhone))
                    throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]));

                dto.HQPhone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.HQPhone);
            }
        }
        else
            entity.HQPhone = null;

        // Self / circular ParentCompany reference check — BEFORE Base() (pure validation).
        var parentCompanyId = string.IsNullOrWhiteSpace(dto.ParentCompany?.Value) ? null : dto.ParentCompany?.Value?.ToLong();

        if (actionType == ActionTypes.Update && parentCompanyId != null)
        {
            if (parentCompanyId == entity.ID)
                throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Parent company and the child company should not be the same"]));

            var parentId = parentCompanyId;
            do
            {
                var parent = db.Companies.AsNoTracking().FirstOrDefault(x => x.ID == parentId);
                if (parent == null) break;
                if (parent.ID == entity.ID)
                    throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Circular reference is not allowed"]));
                parentId = parent.ParentCompanyID;
            } while (parentCompanyId != null);
        }

        var saved = await context.Base();

        // ParentCompanyID (kept verbatim from the old repo — authoritative even though the FK convention also sets it).
        saved.ParentCompanyID = parentCompanyId;

        // CustomFields password-preserving merge — AFTER Base() (needs the loaded dict; the mapper IgnoreEntity's it).
        if (dto.CustomFields != null)
        {
            saved.CustomFields ??= new();
            foreach (var key in dto.CustomFields.Keys)
            {
                var srcField = dto.CustomFields[key];
                if (srcField.IsPassword && srcField.Value == null)
                    continue;

                saved.CustomFields[key] = new CustomField
                {
                    Value = srcField.Value,
                    DisplayName = srcField.DisplayName,
                    IsPassword = srcField.IsPassword,
                    IsEncrypted = srcField.IsEncrypted
                };
            }
        }

        return saved;
    }
}
