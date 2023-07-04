﻿using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories
{
    public class CompanyBranchRepository :
        ShiftRepository<CompanyBranch>,
        IShiftRepositoryAsync<CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
    {
        private readonly ShiftIdentityDB db;

        public CompanyBranchRepository(ShiftIdentityDB db) : base(db, db.CompanyBranches)
        {
            this.db = db;
        }

        public ValueTask<CompanyBranch> CreateAsync(CompanyBranchDTO dto, long? userId = null)
        {
            var entity = new CompanyBranch().CreateShiftEntity(userId);

            AssignValues(dto, entity);

            return new ValueTask<CompanyBranch>(entity);
        }

        public ValueTask<CompanyBranch> DeleteAsync(CompanyBranch entity, long? userId = null)
        {
            entity.DeleteShiftEntity(userId);
            return new ValueTask<CompanyBranch>(entity);
        }

        public async Task<CompanyBranch> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false)
        {
            return await base.FindAsync(id,
                asOf,
                ignoreGlobalFilters: ignoreGlobalFilters,
                x => x.Include(y => y.Company),
                x => x.Include(y => y.Region),
                x => x.Include(y => y.CompanyBranchDepartments).ThenInclude(y => y.Department),
                x => x.Include(y => y.CompanyBranchServices).ThenInclude(y => y.Service)
            );
        }

        public IQueryable<CompanyBranchListDTO> OdataList(bool ignoreGlobalFilters = false)
        {
            var data = db.CompanyBranches.AsNoTracking();

            if (ignoreGlobalFilters)
                data = data.IgnoreQueryFilters();

            return data.Select(x => (CompanyBranchListDTO)x);
        }

        public ValueTask<CompanyBranch> UpdateAsync(CompanyBranch entity, CompanyBranchDTO dto, long? userId = null)
        {
            entity.UpdateShiftEntity(userId);

            AssignValues(dto, entity);

            return new ValueTask<CompanyBranch>(entity);
        }

        public ValueTask<CompanyBranchDTO> ViewAsync(CompanyBranch entity)
        {
            return new ValueTask<CompanyBranchDTO>(entity);
        }

        private void AssignValues(CompanyBranchDTO dto, CompanyBranch entity)
        {
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
                if (!UserRepository.PhoneIsValid(dto.Phone))
                    throw new ShiftEntityException(new Message("Validation Error", "Invalid Phone Number"));

                entity.Phone = UserRepository.GetFormattedPhone(dto.Phone);
            }

            entity.ReloadAfterSave = true;
        }
    }
}
