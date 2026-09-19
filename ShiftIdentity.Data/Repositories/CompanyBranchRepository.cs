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
    // central. The Includes + the mapping customizations live HERE in the base-ctor builder (not
    // IConfiguresShiftRepository — built-in-only, and a pair is configured in one place).
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

            r.Mapping(m =>
            {
                // ── VIEW ── (Company/City ShiftEntitySelectDTOs get Value+Text from the select convention + Includes)
                // Latitude/Longitude are string on the entity and decimal? on the DTO. Both directions are
                // convention-covered, and the conversion parses and formats with the INVARIANT culture — the
                // hand-written pair used decimal.Parse / ToString without one, so on a server whose locale uses
                // a comma for the decimal point "51.5074" read back as 515074 and saved values were unparsable.
                // CustomFields: a dictionary of DTOs would nest on its own, but the read side strips passwords.
                m.View.ForMember(d => d.CustomFields, opt => opt.MapFrom(e => e.CustomFields == null ? null : e.CustomFields
                    .ToDictionary(x => x.Key, x => new CustomFieldDTO
                    {
                        DisplayName = x.Value.DisplayName,
                        IsPassword = x.Value.IsPassword,
                        IsEncrypted = x.Value.IsEncrypted,
                        Value = x.Value.IsPassword ? null : x.Value.Value,
                        HasValue = x.Value.Value != null
                    })));
                // The M:N selects read through explicit junction rows, so the element convention (which reads a
                // navigation collection of the member's name) does not reach them: one line each.
                m.View.ForMember(d => d.Departments, opt => opt.MapFrom(e => e.CompanyBranchDepartments == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }).ToList()));
                m.View.ForMember(d => d.Services, opt => opt.MapFrom(e => e.CompanyBranchServices == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }).ToList()));
                m.View.ForMember(d => d.Brands, opt => opt.MapFrom(e => e.CompanyBranchBrands == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchBrands.Select(y => new ShiftEntitySelectDTO { Value = y.BrandID.ToString()!, Text = y.Brand!.Name }).ToList()));

                // ── ENTITY ── (the hook owns CustomFields/Phone/ShortPhone)
                // PublishTargets is IReadOnlyCollection<PublishTarget> on the DTO (MudSelectExtended's
                // SelectedValues) and List<PublishTarget> on the entity. Same element type, different container,
                // which the collection conversion adapts — it used to be silently dropped on save.
                m.Entity.ForMember(e => e.CustomFields, opt => opt.Ignore());
                m.Entity.ForMember(e => e.Phone, opt => opt.Ignore());
                m.Entity.ForMember(e => e.ShortPhone, opt => opt.Ignore());

                // ── LIST ── (flattened names/display-orders + M:N projections; reproduce the profile ListDTO ForMembers)
                m.List.ForMember(d => d.Company, opt => opt.MapFrom(e => e.Company != null ? e.Company.Name : null));
                m.List.ForMember(d => d.Region, opt => opt.MapFrom(e => e.Region != null ? e.Region.Name : null));
                m.List.ForMember(d => d.City, opt => opt.MapFrom(e => e.City != null ? e.City.Name : null));
                // The scope ids — CompanyId/CityId/RegionId (string) from CompanyID/CityID/RegionID (long?) —
                // are projected by convention: matching ignores case, and long? -> string is a standard
                // conversion.
                //
                // Worth keeping in mind if anyone is tempted to Ignore them on the list: they are the targets of
                // a LIST filter (data-level access, and the Team form's branch picker sending $filter=CompanyId
                // eq X). With no scalar to bind to, EF inlines this whole collection-bearing projection into the
                // WHERE and cannot translate it. Projecting them lets EF bind the Where to e.CompanyID and push
                // it down, leaving the collections in the SELECT.
                m.List.ForMember(d => d.CompanyTerminationDate, opt => opt.MapFrom(e => e.Company != null ? e.Company.TerminationDate : null));
                m.List.ForMember(d => d.CountryDisplayOrder, opt => opt.MapFrom(e => e.City != null && e.City.Region != null && e.City.Region.Country != null ? e.City.Region.Country.DisplayOrder : null));
                m.List.ForMember(d => d.RegionDisplayOrder, opt => opt.MapFrom(e => e.City != null && e.City.Region != null ? e.City.Region.DisplayOrder : null));
                m.List.ForMember(d => d.CityDisplayOrder, opt => opt.MapFrom(e => e.City != null ? e.City.DisplayOrder : null));
                m.List.ForMember(d => d.CompanyDisplayOrder, opt => opt.MapFrom(e => e.Company != null ? e.Company.DisplayOrder : null));
                m.List.ForMember(d => d.Brands, opt => opt.MapFrom(e => e.CompanyBranchBrands.Select(x => new ShiftEntitySelectDTO { Value = x.BrandID.ToString(), Text = x.Brand!.Name }).ToList()));
                m.List.ForMember(d => d.Departments, opt => opt.MapFrom(e => e.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }).ToList()));
                m.List.ForMember(d => d.Services, opt => opt.MapFrom(e => e.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }).ToList()));
            });
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
