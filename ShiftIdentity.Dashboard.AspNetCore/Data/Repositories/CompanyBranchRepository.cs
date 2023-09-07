using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories
{
    public class CompanyBranchRepository :
        ShiftRepository<ShiftIdentityDB, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO, CompanyBranchDTO>,
        IShiftRepositoryAsync<CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
    {
        public CompanyBranchRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.CompanyBranches, mapper)
        {
        }

        public async override Task<CompanyBranch> FindAsync(long id, DateTime? asOf = null)
        {
            return await base.FindAsync(id,
                asOf,
                x => x.Include(y => y.Company),
                x => x.Include(y => y.Region),
                x => x.Include(y => y.CompanyBranchDepartments).ThenInclude(y => y.Department),
                x => x.Include(y => y.CompanyBranchServices).ThenInclude(y => y.Service)
            );
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
                db.CompanyBranchDepartments.Remove(item);

            foreach (var item in entity.CompanyBranchServices.ToList())
                db.CompanyBranchServices.Remove(item);

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

            if (!string.IsNullOrWhiteSpace(dto.Phone))
            {
                if (!Core.ValidatorsAndFormatters.PhoneNumber.PhoneIsValid(dto.Phone))
                    throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

                entity.Phone = Core.ValidatorsAndFormatters.PhoneNumber.GetFormattedPhone(dto.Phone);
            }

            entity.ReloadAfterSave = true;

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
