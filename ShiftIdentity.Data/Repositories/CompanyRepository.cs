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
// (phone/circular-ref/CustomFields merge), the protected-row guard + feature lock are central. The mapping
// customizations live HERE in the base-ctor builder (not IConfiguresShiftRepository — that is built-in-only, and a
// pair is configured in one place).
public class CompanyRepository : ShiftRepository<ShiftIdentityDbContext, Company, CompanyListDTO, CompanyDTO>
{
    public CompanyRepository(
        ShiftIdentityDbContext db,
        ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions)
        : base(db, o => o.Mapping(m =>
        {
            // VIEW — CustomFields with read-side password strip (reproduces the profile Company→CompanyDTO ForMember).
            m.View.ForMember(d => d.CustomFields, opt => opt.MapFrom(e => e.CustomFields == null ? null : e.CustomFields
                .ToDictionary(x => x.Key, x => new CustomFieldDTO
                {
                    DisplayName = x.Value.DisplayName,
                    IsPassword = x.Value.IsPassword,
                    IsEncrypted = x.Value.IsEncrypted,
                    Value = x.Value.IsPassword ? null : x.Value.Value,
                    HasValue = x.Value.Value != null
                })));

            // VIEW — ParentCompany select DTO (Value only; the profile left Text null — no Include on ParentCompany).
            m.View.ForMember(d => d.ParentCompany, opt => opt.MapFrom(e => new ShiftEntitySelectDTO { Value = e.ParentCompanyID.ToString()! }));

            // ENTITY — leave the loaded CustomFields dict intact so the hook's password-preserving merge owns it.
            m.Entity.ForMember(e => e.CustomFields, opt => opt.Ignore());

            // LIST — flattened parent name + the Brands aggregation (reproduce the profile Company→CompanyListDTO).
            m.List.ForMember(d => d.ParentCompanyName, opt => opt.MapFrom(e => e.ParentCompany == null ? null : e.ParentCompany.Name));
            // ParentCompanyID needs nothing here: string? on the DTO ← long? on the entity, and the names match,
            // so the list convention binds the same scalar in the same position. It therefore stays filterable —
            // $filter=ParentCompanyID eq X still translates, and EF still does not inline the Brands aggregation
            // into the WHERE.
            m.List.ForMember(d => d.Brands, opt => opt.MapFrom(e => e.CompanyBranches!
                .SelectMany(x => x.CompanyBranchBrands!)
                .Select(x => x.BrandID).Distinct()
                .Select(x => new ShiftEntitySelectDTO { Value = x.ToString() }).ToList()));
        }))
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
