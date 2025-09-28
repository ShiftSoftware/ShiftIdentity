using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using System.Net;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories
{
    public class CompanyBranchRepository : ShiftRepository<ShiftIdentityDbContext, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
    {
        private readonly CityRepository cityRepository;
        private readonly RegionRepository regionRepo;
        private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
        private readonly ShiftIdentityLocalizer Loc;
        private readonly IConfiguration configuration;

        public CompanyBranchRepository(
            ShiftIdentityDbContext db,
            CityRepository cityRepository,
            RegionRepository regionRepo,
            ShiftIdentityFeatureLocking shiftIdentityFeatureLocking,
            ShiftIdentityLocalizer Loc,
            ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions,
            IConfiguration configuration
        ) : base(db, r =>
            r.IncludeRelatedEntitiesWithFindAsync(
                x => x.Include(y => y.Company),
                x => x.Include(y => y.City).ThenInclude(y=> y.Region).ThenInclude(y=> y.Country), //Region is Required for Replication Model
                x => x.Include(y => y.CompanyBranchDepartments).ThenInclude(y => y.Department),
                x => x.Include(y => y.CompanyBranchServices).ThenInclude(y => y.Service),
                x => x.Include(y => y.CompanyBranchBrands).ThenInclude(y => y.Brand)
            )
        )
        {
            this.cityRepository = cityRepository;
            this.regionRepo = regionRepo;
            this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
            this.Loc = Loc;
            this.configuration = configuration;
            this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
        }

        public override async ValueTask<CompanyBranch> UpsertAsync(CompanyBranch entity, CompanyBranchDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
        {
            if (entity.BuiltIn)
                throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

            var oldRegionId = entity.RegionID;
            var oldCountryId = entity.CountryID;

            entity.Name = dto.Name;
            entity.ShortCode = dto.ShortCode;
            entity.IntegrationId = dto.IntegrationId;
            entity.CompanyID = dto.Company.Value.ToLong();
            entity.CityID = dto.City.Value.ToLong();
            entity.Phones = dto.Phones;
            entity.Emails = dto.Emails;
            entity.TerminationDate = dto.TerminationDate;
            entity.RegionID = (await this.cityRepository.FindAsync(entity.CityID.Value, disableDefaultDataLevelAccess: true, disableGlobalFilters: true))!.RegionID;

            if (entity.RegionID is not null)
                entity.CountryID = (await this.regionRepo.FindAsync(entity.RegionID.GetValueOrDefault(), disableDefaultDataLevelAccess: true, disableGlobalFilters: true))?.CountryID;

            if (actionType == ActionTypes.Insert)
                entity.CustomFields = dto.CustomFields?.ToDictionary(x => x.Key, x => new CustomField
                {
                    Value = x.Value.Value,
                    IsPassword = x.Value.IsPassword,
                    DisplayName = x.Value.DisplayName,
                    IsEncrypted = x.Value.IsEncrypted
                });
            else if (actionType == ActionTypes.Update)
            {
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

            if (actionType == ActionTypes.Update)
            {
                if (entity.RegionID != oldRegionId || entity.CompanyID != dto.Company.Value.ToLong())
                    throw new ShiftEntityException(new Message(Loc["Error"], Loc["Company and Region can not be changed after creation."]));

                if (entity.CountryID != oldCountryId && oldCountryId is not null)
                    throw new ShiftEntityException(new Message(Loc["Error"], Loc["Country can not be changed after creation."]));
            }

            //Update departments
            var deletedDepartments = entity.CompanyBranchDepartments.Where(x => !dto.Departments.Select(s => s.Value.ToLong())
                .Any(s => s == x.DepartmentID));
            var addedDepartments = dto.Departments.Where(x => !entity.CompanyBranchDepartments.Select(s => s.DepartmentID)
                .Any(s => s == x.Value.ToLong()));

            db.CompanyBranchDepartments.RemoveRange(deletedDepartments);
            await db.CompanyBranchDepartments.AddRangeAsync(addedDepartments.Select(x => new CompanyBranchDepartment
            {
                DepartmentID = x.Value.ToLong(),
                CompanyBranch = entity
            }).ToList());

            //Update services
            var deletedServices = entity.CompanyBranchServices.Where(x => !dto.Services.Select(s => s.Value.ToLong())
                .Any(s => s == x.ServiceID));
            var addedServices = dto.Services.Where(x => !entity.CompanyBranchServices.Select(s => s.ServiceID)
                .Any(s => s == x.Value.ToLong()));

            db.CompanyBranchServices.RemoveRange(deletedServices);
            await db.CompanyBranchServices.AddRangeAsync(addedServices.Select(x => new CompanyBranchService
            {
                ServiceID = x.Value.ToLong(),
                CompanyBranch = entity
            }).ToList());

            //Update brands
            var deletedBrands = entity.CompanyBranchBrands.Where(x => !dto.Brands.Select(s => s.Value.ToLong())
                           .Any(s => s == x.BrandID));
            var addedBrands = dto.Brands.Where(x => !entity.CompanyBranchBrands.Select(s => s.BrandID)
                .Any(s => s == x.Value.ToLong()));

            db.CompanyBranchBrands.RemoveRange(deletedBrands);
            await db.CompanyBranchBrands.AddRangeAsync(addedBrands.Select(x => new CompanyBranchBrand
            {
                BrandID = x.Value.ToLong(),
                CompanyBranch = entity
            }).ToList());

            entity.Email = dto.Email;
            entity.Address = dto.Address;

            entity.Longitude = dto.Longitude;
            entity.Latitude = dto.Latitude;
            entity.Photos = JsonSerializer.Serialize(dto.Photos);
            entity.WorkingHours = dto.WorkingHours;
            entity.WorkingDays = dto.WorkingDays;

            //ef core may not set the entity state as Modified if only the collections are changed (CompanyBranchDepartments, CompanyBranchServices)
            if (actionType == ActionTypes.Update)
                db.Entry(entity).State = EntityState.Modified;

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

            if (!string.IsNullOrWhiteSpace(dto.ShortPhone))
                entity.ShortPhone = dto.ShortPhone;
            else
                entity.ShortPhone = null;

            return entity;
        }

        public override ValueTask<CompanyBranch> DeleteAsync(CompanyBranch entity, bool isHardDelete = false, long? userId = null, bool disableDefaultDataLevelAccess = false)
        {
            if (entity.BuiltIn)
                throw new ShiftEntityException(new Message(Loc["Error"], Loc["Built-In Data can't be modified."]), (int)HttpStatusCode.Forbidden);

            return base.DeleteAsync(entity, isHardDelete, userId, disableDefaultDataLevelAccess);
        }

        public override Task SaveChangesAsync()
        {
            if (shiftIdentityFeatureLocking.CompanyBranchFeatureIsLocked)
                throw new ShiftEntityException(new Message(Loc["Error"], Loc["Company Branch Feature is locked"]));

            return base.SaveChangesAsync();
        }
    }
}