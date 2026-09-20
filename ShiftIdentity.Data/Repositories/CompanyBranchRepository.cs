using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Enums;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Collections.Generic;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories
{
    // THIN repository (Rung C): CompanyBranch's CRUD is attribute-driven, but the endpoint routes through this
    // repository (not the built-in one) solely because ApplyPostODataProcessing shapes the list query — there is
    // no entity hook for that. All write logic (City/Region derivation, M:N sync, CustomFields, phone, lat/long)
    // moved to the CompanyBranch entity's IUpsertsShiftRepository hook; the protected-row guard + feature lock are
    // central. The Includes live HERE in the base-ctor builder (not IConfiguresShiftRepository — built-in-only);
    // the mapping customizations are in the mapper class (Mappers/ShiftIdentityMapper.cs), because what a member
    // maps from is not the repository's business.
    public class CompanyBranchRepository : ShiftRepository<ShiftIdentityDbContext, CompanyBranch, CompanyBranchListDTO, CompanyBranchDTO>
    {
        public CompanyBranchRepository(ShiftIdentityDbContext db) : base(db, r =>
        {
            r.IncludeRelatedEntitiesWithFindAsync(
                x => x.Include(y => y.Company),
                x => x.Include(y => y.City).ThenInclude(y => y.Region).ThenInclude(y => y.Country), //Region is Required for Replication Model
                x => x.Include(y => y.CompanyBranchDepartments).ThenInclude(y => y.Department),
                x => x.Include(y => y.CompanyBranchServices).ThenInclude(y => y.Service),
                x => x.Include(y => y.CompanyBranchBrands).ThenInclude(y => y.Brand)
            );
        })
        {
        }

        public override ValueTask<IQueryable<CompanyBranchListDTO>> ApplyPostODataProcessing(IQueryable<CompanyBranchListDTO> queryable)
        {
            if (!queryable.HasWhereOnProperty(x => x.TerminationDate) && !queryable.HasWhereOnProperty(x => x.CompanyTerminationDate))
                queryable = queryable.Where(x => x.TerminationDate == null && x.CompanyTerminationDate == null);

            return new(queryable);
        }
    }
}
