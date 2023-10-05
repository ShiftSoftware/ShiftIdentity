using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories
{
    public class CompanyBranchRepository : ShiftRepository<ShiftIdentityDB, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO, CompanyBranchDTO>
    {
        public CompanyBranchRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.CompanyBranches, mapper, r =>
            r.IncludeRelatedEntitiesWithFindAsync(
                x => x.Include(y => y.Company),
                x => x.Include(y => y.Region),
                x => x.Include(y => y.CompanyBranchDepartments).ThenInclude(y => y.Department),
                x => x.Include(y => y.CompanyBranchServices).ThenInclude(y => y.Service)
            )
        )
        {
        }

        public override ValueTask<CompanyBranch> UpsertAsync(CompanyBranch entity, CompanyBranchDTO dto, ActionTypes actionType, long? userId = null)
        {
            if (entity.BuiltIn)
                throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

            if (actionType == ActionTypes.Update)
            {
                if (entity.RegionID != dto.Region.Value.ToLong() || entity.CompanyID != dto.Company.Value.ToLong())
                    throw new ShiftEntityException(new Message("Error", $"Company and Region can not be changed after creation."));
            }

            entity.Name = dto.Name;
            entity.ShortCode = dto.ShortCode;
            entity.ExternalId = dto.ExternalId;
            entity.CompanyID = dto.Company.Value.ToLong();
            entity.RegionID = dto.Region.Value.ToLong();

            foreach (var item in entity.CompanyBranchDepartments.ToList())
                db.CompanyBranchDepartments.Remove(item); //We use db.CompanyBranchDepartments instead of entity.CompanyBranchDepartments. Because if we use entity.CompanyBranchDepartments then the record in CompanyBranchDepartments table is not removed, only the CompanyBrancID is set to null

            foreach (var item in entity.CompanyBranchServices.ToList())
                db.CompanyBranchServices.Remove(item); //We use db.CompanyBranchServices instead of entity.CompanyBranchServices. Because if we use entity.CompanyBranchServices then the record in CompanyBranchServices table is not removed, only the CompanyBrancID is set to null

            entity.CompanyBranchDepartments = dto.Departments.Select(x => new CompanyBranchDepartment
            {
                DepartmentID = x.Value.ToLong()
            }).ToList();

            entity.CompanyBranchServices = dto.Services.Select(x => new CompanyBranchService
            {
                ServiceID = x.Value.ToLong()
            }).ToList();

            entity.Email = dto.Email;
            entity.Address = dto.Address;

            //ef core may not set the entity state as Modified if the only the collections are changed (CompanyBranchDepartments, CompanyBranchServices)
            if (actionType == ActionTypes.Update)
                db.Entry(entity).State = EntityState.Modified;

            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                    throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

                entity.Phone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
            }

            return new ValueTask<CompanyBranch>(entity);
        }

        public override ValueTask<CompanyBranch> DeleteAsync(CompanyBranch entity, bool isHardDelete = false, long? userId = null)
        {
            if (entity.BuiltIn)
                throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

            return base.DeleteAsync(entity, isHardDelete, userId);
        }
    }
}
