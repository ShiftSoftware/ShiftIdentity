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
// (phone/circular-ref/CustomFields merge), the protected-row guard + feature lock are central. The mapper config
// lives HERE in the base-ctor builder (not IConfiguresShiftRepository — that is built-in-only and would trip
// SHENGEN006 against this builder; and UseGeneratedMapper=true is illegal on the custom-repository attribute).
public class CompanyRepository : ShiftRepository<ShiftIdentityDbContext, Company, CompanyListDTO, CompanyDTO>
{
    public CompanyRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions)
        : base(db, o => o.UseGeneratedMapper(map => map

            // VIEW — CustomFields with read-side password strip (reproduces the profile Company→CompanyDTO ForMember).
            .ForView(d => d.CustomFields, e => e.CustomFields == null ? null : e.CustomFields
                .ToDictionary(x => x.Key, x => new CustomFieldDTO
                {
                    DisplayName = x.Value.DisplayName,
                    IsPassword = x.Value.IsPassword,
                    IsEncrypted = x.Value.IsEncrypted,
                    Value = x.Value.IsPassword ? null : x.Value.Value,
                    HasValue = x.Value.Value != null
                }))

            // VIEW — ParentCompany select DTO (Value only; the profile left Text null — no Include on ParentCompany).
            .ForView(d => d.ParentCompany, e => new ShiftEntitySelectDTO { Value = e.ParentCompanyID.ToString()! })

            // ENTITY — leave the loaded CustomFields dict intact so the hook's password-preserving merge owns it.
            .IgnoreEntity(e => e.CustomFields)

            // LIST — flattened parent name + the Brands aggregation (reproduce the profile Company→CompanyListDTO).
            .ForList(d => d.ParentCompanyName, e => e.ParentCompany == null ? null : e.ParentCompany.Name)
            // ParentCompanyID is string in the DTO but long? on the entity — the generated list convention doesn't do
            // long?→string, so project it explicitly (also keeps it filterable: a $filter=ParentCompanyID eq X needs a
            // bound scalar or EF inlines the Brands-aggregation-bearing projection into the WHERE, same failure mode
            // as CompanyBranch's scope-ids).
            .ForList(d => d.ParentCompanyID, e => e.ParentCompanyID.HasValue ? e.ParentCompanyID.Value.ToString() : null)
            .ForList(d => d.Brands, e => e.CompanyBranches!
                .SelectMany(x => x.CompanyBranchBrands!)
                .Select(x => x.BrandID).Distinct()
                .Select(x => new ShiftEntitySelectDTO { Value = x.ToString() }).ToList())))
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
