using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Dtos;
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
    // central. The Includes + generated-mapper config live HERE in the base-ctor builder (not
    // IConfiguresShiftRepository — built-in-only + SHENGEN006; and UseGeneratedMapper=true is illegal on the
    // custom-repository attribute variant).
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

            r.UseGeneratedMapper(map => map
                // ── VIEW ── (Company/City ShiftEntitySelectDTOs get Value+Text from the FK convention + Includes)
                .ForView(d => d.Latitude, e => string.IsNullOrWhiteSpace(e.Latitude) ? (decimal?)null : decimal.Parse(e.Latitude))
                .ForView(d => d.Longitude, e => string.IsNullOrWhiteSpace(e.Longitude) ? (decimal?)null : decimal.Parse(e.Longitude))
                .ForView(d => d.CustomFields, e => e.CustomFields == null ? null : e.CustomFields
                    .ToDictionary(x => x.Key, x => new CustomFieldDTO
                    {
                        DisplayName = x.Value.DisplayName,
                        IsPassword = x.Value.IsPassword,
                        IsEncrypted = x.Value.IsEncrypted,
                        Value = x.Value.IsPassword ? null : x.Value.Value,
                        HasValue = x.Value.Value != null
                    }))
                .ForView(d => d.Departments, e => e.CompanyBranchDepartments == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }).ToList())
                .ForView(d => d.Services, e => e.CompanyBranchServices == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }).ToList())
                .ForView(d => d.Brands, e => e.CompanyBranchBrands == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchBrands.Select(y => new ShiftEntitySelectDTO { Value = y.BrandID.ToString()!, Text = y.Brand!.Name }).ToList())

                // ── ENTITY ── (lat/long decimal?→string; the hook owns CustomFields/Phone/ShortPhone)
                .ForEntity(e => e.Latitude, dto => dto.Latitude.ToString())
                .ForEntity(e => e.Longitude, dto => dto.Longitude.ToString())
                .IgnoreEntity(e => e.CustomFields)
                .IgnoreEntity(e => e.Phone)
                .IgnoreEntity(e => e.ShortPhone)

                // ── LIST ── (flattened names/display-orders + M:N projections; reproduce the profile ListDTO ForMembers)
                .ForList(d => d.Company, e => e.Company != null ? e.Company.Name : null)
                .ForList(d => d.Region, e => e.Region != null ? e.Region.Name : null)
                .ForList(d => d.City, e => e.City != null ? e.City.Name : null)
                // Scope-id projections are the reason this list is served by the generated mapper again (not AutoMapper):
                // the DTO uses Id (CompanyId/CityId/RegionId, string) but the entity uses ID (CompanyID/…, long?), and
                // BOTH the case AND the type differ — the generated list convention is case-sensitive and doesn't do
                // long?→string, so without these ForLists the filter target CompanyId is never projected. A LIST filter
                // then has no scalar to bind to (data-level / the Team-form branch picker's OData $filter=CompanyId eq X),
                // so EF inlines the whole collection-bearing projection into the WHERE and can't translate it. Projecting
                // CompanyId lets EF bind the Where to e.CompanyID and push it down; the collections stay in the SELECT.
                .ForList(d => d.CompanyId, e => e.CompanyID.HasValue ? e.CompanyID.Value.ToString() : null)
                .ForList(d => d.CityId, e => e.CityID.HasValue ? e.CityID.Value.ToString() : null)
                .ForList(d => d.RegionId, e => e.RegionID.HasValue ? e.RegionID.Value.ToString() : null)
                .ForList(d => d.CompanyTerminationDate, e => e.Company != null ? e.Company.TerminationDate : null)
                .ForList(d => d.CountryDisplayOrder, e => e.City != null && e.City.Region != null && e.City.Region.Country != null ? e.City.Region.Country.DisplayOrder : null)
                .ForList(d => d.RegionDisplayOrder, e => e.City != null && e.City.Region != null ? e.City.Region.DisplayOrder : null)
                .ForList(d => d.CityDisplayOrder, e => e.City != null ? e.City.DisplayOrder : null)
                .ForList(d => d.CompanyDisplayOrder, e => e.Company != null ? e.Company.DisplayOrder : null)
                .ForList(d => d.Brands, e => e.CompanyBranchBrands.Select(x => new ShiftEntitySelectDTO { Value = x.BrandID.ToString(), Text = x.Brand!.Name }).ToList())
                .ForList(d => d.Departments, e => e.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }))
                .ForList(d => d.Services, e => e.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name })));
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
