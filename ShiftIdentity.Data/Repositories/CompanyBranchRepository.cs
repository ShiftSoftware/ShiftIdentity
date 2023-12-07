using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Extensions;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Net;
using System.Text.Json;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories
{
    public class CompanyBranchRepository : ShiftRepository<ShiftIdentityDbContext, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
    {
        private readonly CityRepository cityRepository;
        public CompanyBranchRepository(ShiftIdentityDbContext db, CityRepository cityRepository) : base(db, r =>
            r.IncludeRelatedEntitiesWithFindAsync(
                x => x.Include(y => y.Company),
                x => x.Include(y => y.City),
                x => x.Include(y => y.CompanyBranchDepartments).ThenInclude(y => y.Department),
                x => x.Include(y => y.CompanyBranchServices).ThenInclude(y => y.Service)
            )
        )
        {
            this.cityRepository = cityRepository;
        }

        public override async ValueTask<CompanyBranch> UpsertAsync(CompanyBranch entity, CompanyBranchDTO dto, ActionTypes actionType, long? userId = null)
        {
            if (entity.BuiltIn)
                throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

            var oldRegionId = entity.RegionID;

            entity.Name = dto.Name;
            entity.ShortCode = dto.ShortCode;
            entity.ExternalId = dto.ExternalId;
            entity.CompanyID = dto.Company.Value.ToLong();
            entity.CityID = dto.City.Value.ToLong();
            entity.CustomFields = dto.CustomFields;
            entity.RegionID = (await this.cityRepository.FindAsync(entity.CityID.Value))!.RegionID;

            if (actionType == ActionTypes.Update)
            {
                if (entity.RegionID != oldRegionId || entity.CompanyID != dto.Company.Value.ToLong())
                    throw new ShiftEntityException(new Message("Error", $"Company and Region can not be changed after creation."));
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

            entity.Email = dto.Email;
            entity.Address = dto.Address;

            entity.Longitude = dto.Longitude;
            entity.Latitude = dto.Latitude;
            entity.Photos = JsonSerializer.Serialize(dto.Photos);

            //ef core may not set the entity state as Modified if the only the collections are changed (CompanyBranchDepartments, CompanyBranchServices)
            if (actionType == ActionTypes.Update)
                db.Entry(entity).State = EntityState.Modified;

            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                    throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

                entity.Phone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
            }

            return entity;
        }

        public override ValueTask<CompanyBranch> DeleteAsync(CompanyBranch entity, bool isHardDelete = false, long? userId = null)
        {
            if (entity.BuiltIn)
                throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

            return base.DeleteAsync(entity, isHardDelete, userId);
        }
    }
}
