using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung C): CompanyBranch's CRUD routes through the THIN CompanyBranchRepository (kept
// only for ApplyPostODataProcessing) — hence the custom-repository attribute variant. All write logic moves here
// via IUpsertsShiftRepository: City/Region → RegionID/CountryID derivation, Region/Country immutability,
// CustomFields password-preserve, M:N Departments/Services/Brands sync, and phone normalization. The
// protected-row guard + feature lock are central; the mapper config lives in the repository's builder.
[TemporalShiftEntity]
[Table("CompanyBranches", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<CompanyBranchListDTO, CompanyBranchDTO, ShiftIdentityActions, CompanyBranchRepository>("api/IdentityCompanyBranch", nameof(ShiftIdentityActions.CompanyBranches))]
public class CompanyBranch :
    ShiftEntity<CompanyBranch>,
    IEntityHasRegion<CompanyBranch>,
    IEntityHasCompany<CompanyBranch>,
    IEntityHasCountry<CompanyBranch>,
    IEntityHasCompanyBranch<CompanyBranch>,
    IShiftEntityReplication,
    IShiftEntityProtectable,
    IUpsertsShiftRepository<CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public List<TaggedTextDTO> Phones { get; set; } = new();
    public string? ShortPhone { get; set; }
    public string? Email { get; set; }
    public List<TaggedTextDTO> Emails { get; set; } = new();
    public string? Address { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
    public string? Photos { get; set; }
    public string? MobilePhotos { get; set; }
    public string? WorkingHours { get; set; }
    public string? WorkingDays { get; set; }
    public string? IntegrationId { get; set; } = default!;
    public string? ShortCode { get; set; }
    public bool IsProtected { get; set; }
    public DateTime? TerminationDate { get; set; }
    public Dictionary<string, CustomField>? CustomFields { get; set; }

    public virtual Company? Company { get; set; } = default!;
    public virtual Region? Region { get; set; } = default!;
    public virtual City City { get; set; } = default!;
    public virtual ICollection<CompanyBranchDepartment>? CompanyBranchDepartments { get; set; }
    public virtual ICollection<CompanyBranchService>? CompanyBranchServices { get; set; }
    public virtual ICollection<CompanyBranchBrand>? CompanyBranchBrands { get; set; }
    public virtual ICollection<User> Users { get; set; }

    public long? RegionID { get; set; }
    public long? CityID { get; set; }
    public long? CompanyID { get; set; }
    public long? CountryID { get; set; }
    public long? CompanyBranchID { get; set; }

    public int? DisplayOrder { get; set; }
    public string? DisplayName { get; set; } = default!;
    public string? Description { get; set; }
    public List<PublishTarget>? PublishTargets { get; set; } = new();

    public CompanyBranch()
    {
        CompanyBranchDepartments = new HashSet<CompanyBranchDepartment>();
        CompanyBranchServices = new HashSet<CompanyBranchService>();
        CompanyBranchBrands = new HashSet<CompanyBranchBrand>();
        Users = new HashSet<User>();
        CustomFields = new();
    }

    public async ValueTask<CompanyBranch> UpsertAsync(
        CompanyBranch entity,
        CompanyBranchDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();
        var cityRepository = context.Services.GetRequiredService<ShiftRepository<ShiftIdentityDbContext, City, CityListDTO, CityDTO>>();
        var regionRepo = context.Services.GetRequiredService<ShiftRepository<ShiftIdentityDbContext, Region, RegionListDTO, RegionDTO>>();
        var Loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();
        var configuration = context.Services.GetRequiredService<IConfiguration>();

        var oldRegionId = entity.RegionID;
        var oldCountryId = entity.CountryID;

        // Region/Country derivation from the selected City — BEFORE Base() so the country/region-scoped data-level
        // write check (inside Base()) authorizes against the real region/country. Uses dto.City (entity.CityID
        // isn't mapped until Base()).
        var cityId = dto.City.Value.ToLong();
        entity.RegionID = (await cityRepository.FindAsync(cityId, asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true))!.RegionID;

        if (entity.RegionID is not null)
            entity.CountryID = (await regionRepo.FindAsync(entity.RegionID.GetValueOrDefault(), asOf: null, disableDefaultDataLevelAccess: true, disableGlobalFilters: true))?.CountryID;

        // Region + Country immutability. (The original also compared CompanyID, but that clause was dead code —
        // CompanyID was assigned from dto.Company earlier in the same method, so the comparison never fired.)
        if (actionType == ActionTypes.Update)
        {
            if (entity.RegionID != oldRegionId)
                throw new ShiftEntityException(new Message(Loc["Error"], Loc["Company and Region can not be changed after creation."]));

            if (entity.CountryID != oldCountryId && oldCountryId is not null)
                throw new ShiftEntityException(new Message(Loc["Error"], Loc["Country can not be changed after creation."]));
        }

        // CustomFields (mapper IgnoreEntity's it): insert copies all; update merges, preserving stored passwords.
        if (actionType == ActionTypes.Insert)
            entity.CustomFields = dto.CustomFields?.ToDictionary(x => x.Key, x => new CustomField
            {
                Value = x.Value.Value,
                IsPassword = x.Value.IsPassword,
                DisplayName = x.Value.DisplayName,
                IsEncrypted = x.Value.IsEncrypted
            });
        else if (actionType == ActionTypes.Update && dto.CustomFields != null)
        {
            entity.CustomFields ??= new();
            foreach (var customField in dto.CustomFields)
            {
                if (customField.Value.IsPassword && customField.Value.Value is null)
                    continue;

                entity.CustomFields[customField.Key] = new CustomField
                {
                    IsEncrypted = customField.Value.IsEncrypted,
                    IsPassword = customField.Value.IsPassword,
                    DisplayName = customField.Value.DisplayName,
                    Value = customField.Value.Value
                };
            }
        }

        // M:N Departments / Services / Brands differential sync (the entity's collections are loaded via the
        // repository's Includes on update; empty on insert ⇒ pure adds).
        var deletedDepartments = entity.CompanyBranchDepartments!.Where(x => !dto.Departments.Select(s => s.Value.ToLong()).Any(s => s == x.DepartmentID));
        var addedDepartments = dto.Departments.Where(x => !entity.CompanyBranchDepartments!.Select(s => s.DepartmentID).Any(s => s == x.Value.ToLong()));
        db.CompanyBranchDepartments.RemoveRange(deletedDepartments);
        await db.CompanyBranchDepartments.AddRangeAsync(addedDepartments.Select(x => new CompanyBranchDepartment { DepartmentID = x.Value.ToLong(), CompanyBranch = entity }).ToList());

        var deletedServices = entity.CompanyBranchServices!.Where(x => !dto.Services.Select(s => s.Value.ToLong()).Any(s => s == x.ServiceID));
        var addedServices = dto.Services.Where(x => !entity.CompanyBranchServices!.Select(s => s.ServiceID).Any(s => s == x.Value.ToLong()));
        db.CompanyBranchServices.RemoveRange(deletedServices);
        await db.CompanyBranchServices.AddRangeAsync(addedServices.Select(x => new CompanyBranchService { ServiceID = x.Value.ToLong(), CompanyBranch = entity }).ToList());

        var deletedBrands = entity.CompanyBranchBrands!.Where(x => !dto.Brands.Select(s => s.Value.ToLong()).Any(s => s == x.BrandID));
        var addedBrands = dto.Brands.Where(x => !entity.CompanyBranchBrands!.Select(s => s.BrandID).Any(s => s == x.Value.ToLong()));
        db.CompanyBranchBrands.RemoveRange(deletedBrands);
        await db.CompanyBranchBrands.AddRangeAsync(addedBrands.Select(x => new CompanyBranchBrand { BrandID = x.Value.ToLong(), CompanyBranch = entity }).ToList());

        // Phone validate/format + ShortPhone normalize (mapper IgnoreEntity's Phone/ShortPhone).
        if (!string.IsNullOrWhiteSpace(dto.Phone))
        {
            if (!configuration.GetSection("ShiftIdentity").Exists() || configuration.GetSection("ShiftIdentity:DisablePhoneNumberValidation").Value == "False")
            {
                if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                    throw new ShiftEntityException(new Message(Loc["Validation Error"], Loc["Invalid Phone Number"]));

                entity.Phone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
            }
            else
                entity.Phone = dto.Phone;
        }
        else
            entity.Phone = null;

        entity.ShortPhone = string.IsNullOrWhiteSpace(dto.ShortPhone) ? null : dto.ShortPhone;

        // Base(): MapToEntity (scalars/FK/Photos/lat-long-via-ForEntity), audit, protected-row guard, data-level write check.
        return await context.Base();
    }
}
