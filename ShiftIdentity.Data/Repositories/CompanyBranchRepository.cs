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
                // Latitude/Longitude are string on the entity and decimal? on the DTO. Both directions are now
                // convention-covered, and the convention parses and formats with the INVARIANT culture — the
                // hand-written pair used decimal.Parse / ToString without one, so on a server whose locale uses
                // a comma for the decimal point "51.5074" read back as 515074 and saved values were unparsable.
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

                // ── ENTITY ── (the hook owns CustomFields/Phone/ShortPhone)
                // PublishTargets is IReadOnlyCollection<PublishTarget> on the DTO (MudSelectExtended's
                // SelectedValues) and List<PublishTarget> on the entity. Same element type, different container,
                // which the collection convention now adapts — it used to be silently dropped on save.
                .IgnoreEntity(e => e.CustomFields)
                .IgnoreEntity(e => e.Phone)
                .IgnoreEntity(e => e.ShortPhone)

                // ── LIST ── (flattened names/display-orders + M:N projections; reproduce the profile ListDTO ForMembers)
                .ForList(d => d.Company, e => e.Company != null ? e.Company.Name : null)
                .ForList(d => d.Region, e => e.Region != null ? e.Region.Name : null)
                .ForList(d => d.City, e => e.City != null ? e.City.Name : null)
                // The scope ids — CompanyId/CityId/RegionId (string) from CompanyID/CityID/RegionID (long?) —
                // are now projected by convention: matching ignores case by default, and long? -> string is a
                // standard list conversion. They used to need a hand-written ForList each because the generator
                // did neither.
                //
                // Worth keeping in mind if anyone is tempted to IgnoreList them: they are the targets of a LIST
                // filter (data-level access, and the Team form's branch picker sending $filter=CompanyId eq X).
                // With no scalar to bind to, EF inlines this whole collection-bearing projection into the WHERE
                // and cannot translate it. Projecting them lets EF bind the Where to e.CompanyID and push it
                // down, leaving the collections in the SELECT.
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
