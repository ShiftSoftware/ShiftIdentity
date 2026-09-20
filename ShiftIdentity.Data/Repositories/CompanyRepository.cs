using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// THIN repository (Rung C): Company's CRUD is attribute-driven, but the endpoint routes through this repository
// (not the built-in one) solely because ApplyPostODataProcessing must shape the list query — there is no entity
// hook for that. Everything else moved off: write logic → the Company entity's IUpsertsShiftRepository hook
// (phone/circular-ref/CustomFields merge), the protected-row guard + feature lock are central, and the mapping
// customizations are in the mapper class (Mappers/ShiftIdentityMapper.cs) — what a member maps from is not the
// repository's business.
public class CompanyRepository : ShiftRepository<ShiftIdentityDbContext, Company, CompanyListDTO, CompanyDTO>
{
    public CompanyRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions)
        : base(db)
    {
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override ValueTask<IQueryable<CompanyListDTO>> ApplyPostODataProcessing(IQueryable<CompanyListDTO> queryable)
    {
        if (!queryable.HasWhereOnProperty(x => x.TerminationDate))
            queryable = queryable.Where(x => x.TerminationDate == null);

        return new(queryable);
    }
}
