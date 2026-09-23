using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ShiftMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.City;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Region;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Team;
using ShiftSoftware.ShiftIdentity.Core.DTOs.User;
using ShiftSoftware.ShiftIdentity.Data.Entities;

namespace ShiftSoftware.ShiftIdentity.Data.Mappers;

/// <summary>
/// The ONE place ShiftIdentity customizes its CRUD maps. Every identity triple's four maps (entity ↔ view,
/// entity → list, entity → entity) are declared automatically — by the endpoint attributes on the entities and
/// by the repository classes closing <c>ShiftRepository&lt;,,,&gt;</c> — and most need nothing. The pairs written
/// here are the ones convention cannot do (flattened names through navigations, M:N junctions to select lists,
/// the password strip on custom fields, the hashid-encoded ids inside the calendar's JSON children); each
/// <c>CreateMap</c> REPLACES the automatic map for that pair (SM0047, informational) and the other pairs of the
/// triple stay automatic. Nothing about mapping is written in a repository or an entity — what a member maps
/// from is not their business; a repository's only word about its maps is how deep they nest.
/// <para>
/// An ordinary mapper class: not partial, nothing injects it, nothing registers it by name — the identity
/// registrations register this assembly's generated mapper, which carries these maps. The replication pairs
/// are a separate class (<c>Replication/ShiftIdentityReplicationMapper.cs</c>): different pairs, different job.
/// </para>
/// <para>
/// Two things a <c>CreateMap</c> does not inherit from the automatic map it replaces, so both are written: the
/// write map is declared as the REVERSE of the view map (the marker says <c>Reverse = true</c>; a DTO is a subset
/// of its entity, so the entity-only members it leaves untouched are the quiet SM0006 rather than one SM0001
/// warning each), and flattening is off (<see cref="ConfigureDefaults"/>) — a repository never silently reaches
/// two levels into an entity by name alone, and replacing a map should change nothing but the members written.
/// </para>
/// <para>
/// The hash-id service is read from <see cref="ShiftMapperBase.Services"/> WHEN A CALENDAR GROUP IS MAPPED rather
/// than injected into the constructor, on purpose: a generated mapper constructs every class it composes the
/// first time anything is mapped, so a constructor dependency would make the whole identity mapper unusable
/// outside a container — <c>Mapper.Create(assembly)</c>, which the replication goldens and any host without DI
/// rely on. Read this way, only a map of a calendar group needs the container.
/// </para>
/// </summary>
public class ShiftIdentityMapper : ShiftMapperBase
{
    public ShiftIdentityMapper()
    {
        var hashIds = new Lazy<IHashIdService>(() => Services.GetRequiredService<IHashIdService>());

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // Region — LIST: Country (name) and CountryDisplayOrder reach through the Country navigation.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<Region, RegionListDTO>()
            .ForMember(d => d.Country, opt => opt.MapFrom(e => e.Country != null ? e.Country.Name : null))
            .ForMember(d => d.CountryDisplayOrder, opt => opt.MapFrom(e => e.Country != null ? e.Country.DisplayOrder : null));

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // City — LIST: the four flattened columns (Region/Country names + display orders), spliced into the SQL.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<City, CityListDTO>()
            .ForMember(d => d.Region, opt => opt.MapFrom(e => e.Region != null ? e.Region.Name : null))
            .ForMember(d => d.Country, opt => opt.MapFrom(e => e.Region != null && e.Region.Country != null ? e.Region.Country.Name : null))
            .ForMember(d => d.CountryDisplayOrder, opt => opt.MapFrom(e => e.Region != null && e.Region.Country != null ? e.Region.Country.DisplayOrder : null))
            .ForMember(d => d.RegionDisplayOrder, opt => opt.MapFrom(e => e.Region != null ? e.Region.DisplayOrder : null));

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // Team — VIEW: the M:N joins → List<ShiftEntitySelectDTO>. Not convention: the DTO names
        // (Users/CompanyBranches) don't match the join navigations, and Text reaches through .User/.CompanyBranch.
        // The write map stays automatic: it maps the scalars and the Company FK, and the entity's upsert hook
        // reconciles the join rows. Tags is IReadOnlyCollection<string> on the DTO and List<string> on the
        // entity — same element type, different container, which the collection conversion adapts on both legs.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<Team, TeamDTO>()
            .ForMember(d => d.Users, opt => opt.MapFrom(e => e.TeamUsers
                .Select(y => new ShiftEntitySelectDTO { Value = y.UserID.ToString(), Text = y.User.Username }).ToList()))
            .ForMember(d => d.CompanyBranches, opt => opt.MapFrom(e => e.TeamCompanyBranches
                .Select(y => new ShiftEntitySelectDTO { Value = y.CompanyBranchID.ToString(), Text = y.CompanyBranch.Name }).ToList()));

        // Team — LIST: the flattened Company name. CompanyId needs nothing — matching ignores case, so it binds
        // to the entity's CompanyID, and long? -> string is a standard conversion; it is a LIST FILTER target, so
        // it must keep being projected, and the convention is what projects it.
        CreateMap<Team, TeamListDTO>()
            .ForMember(d => d.Company, opt => opt.MapFrom(e => e.Company != null ? e.Company.Name : null));

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // CompanyCalendar — VIEW: the Branches M:N join → List<ShiftEntitySelectDTO> (raw ids; the DTO's
        // CompanyBranchHashIdConverter encodes on the wire). WRITE (the reverse): the join rows are owned by the
        // entity's IUpsertsShiftRepository merge, so the map leaves them alone.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<CompanyCalendar, CompanyCalendarDTO>()
            .ForMember(v => v.Branches, opt => opt.MapFrom(e => e.Branches
                .Select(b => new ShiftEntitySelectDTO { Value = b.CompanyBranchID.ToString() }).ToList()))
            .ReverseMap()
            .ForMember(e => e.Branches, opt => opt.Ignore());

        // CompanyCalendar's JSON children, customized ONCE for every parent that nests them. ShiftGroups and
        // WeekendGroups nest automatically from the DTO graph (view and write), structure and trivial
        // grandchildren alike; what convention cannot do is their Departments/Brands, which are hashid-ENCODED
        // select lists over plain long id lists on the entity side — so the four child pairs are declared here
        // with that one customization each, and ShiftMapper uses them wherever a parent map reaches them.
        //
        // The two shift-group pairs keep a null list as null (AllowNullCollections), in both directions. In a
        // shift group, Days = null means "every day" and an empty list means "no day". CalendarService reads it
        // that way, and so do the readers of the replicated calendar documents. The default option turns null
        // into an empty list, so a plain read or an unchanged save would stop the group from applying on any
        // day. The other lists in these pairs are not null in stored data: DepartmentIds, BrandIds and Shifts
        // start as empty lists, and Departments/Brands are built by the MapFrom lines below.
        CreateMap<CompanyCalendarShiftGroup, CompanyCalendarShiftGroupDTO>(o => o.AllowNullCollections = true)
            .ForMember(d => d.Departments, opt => opt.MapFrom(sg => sg.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<DepartmentListDTO>(id) }).ToList()))
            .ForMember(d => d.Brands, opt => opt.MapFrom(sg => sg.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<BrandListDTO>(id) }).ToList()));

        CreateMap<CompanyCalendarShiftGroupDTO, CompanyCalendarShiftGroup>(o => o.AllowNullCollections = true)
            .ForMember(sg => sg.DepartmentIds, opt => opt.MapFrom(d => d.Departments.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<DepartmentListDTO>(x.Value!)).ToList()))
            .ForMember(sg => sg.BrandIds, opt => opt.MapFrom(d => d.Brands.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<BrandListDTO>(x.Value!)).ToList()));

        CreateMap<CompanyCalendarWeekendGroup, CompanyCalendarWeekendGroupDTO>()
            .ForMember(d => d.Departments, opt => opt.MapFrom(wg => wg.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<DepartmentListDTO>(id) }).ToList()))
            .ForMember(d => d.Brands, opt => opt.MapFrom(wg => wg.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = hashIds.Value.Encode<BrandListDTO>(id) }).ToList()));

        CreateMap<CompanyCalendarWeekendGroupDTO, CompanyCalendarWeekendGroup>()
            .ForMember(wg => wg.DepartmentIds, opt => opt.MapFrom(d => d.Departments.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<DepartmentListDTO>(x.Value!)).ToList()))
            .ForMember(wg => wg.BrandIds, opt => opt.MapFrom(d => d.Brands.Where(x => x.Value != null).Select(x => hashIds.Value.Decode<BrandListDTO>(x.Value!)).ToList()));

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // CompanyBranch — VIEW (Company/City ShiftEntitySelectDTOs get Value+Text from the select convention +
        // the repository's Includes). Latitude/Longitude are string on the entity and decimal? on the DTO: both
        // directions are convention-covered, and the conversion parses and formats with the INVARIANT culture —
        // the hand-written pair used decimal.Parse / ToString without one, so on a server whose locale uses a
        // comma for the decimal point "51.5074" read back as 515074 and saved values were unparsable.
        // CustomFields: a dictionary of DTOs would nest on its own, but the read side strips passwords. The M:N
        // selects read through explicit junction rows, so the element convention (which reads a navigation
        // collection of the member's name) does not reach them: one line each.
        // WRITE (the reverse): the entity's upsert hook owns CustomFields/Phone/ShortPhone. PublishTargets is
        // IReadOnlyCollection<PublishTarget> on the DTO (MudSelectExtended's SelectedValues) and
        // List<PublishTarget> on the entity — same element type, different container, which the collection
        // conversion adapts; it used to be silently dropped on save.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<CompanyBranch, CompanyBranchDTO>()
            .ForMember(d => d.CustomFields, opt => opt.MapFrom(e => e.CustomFields == null ? null : e.CustomFields
                .ToDictionary(x => x.Key, x => new CustomFieldDTO
                {
                    DisplayName = x.Value.DisplayName,
                    IsPassword = x.Value.IsPassword,
                    IsEncrypted = x.Value.IsEncrypted,
                    Value = x.Value.IsPassword ? null : x.Value.Value,
                    HasValue = x.Value.Value != null
                })))
            .ForMember(d => d.Departments, opt => opt.MapFrom(e => e.CompanyBranchDepartments == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }).ToList()))
            .ForMember(d => d.Services, opt => opt.MapFrom(e => e.CompanyBranchServices == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }).ToList()))
            .ForMember(d => d.Brands, opt => opt.MapFrom(e => e.CompanyBranchBrands == null ? new List<ShiftEntitySelectDTO>() : e.CompanyBranchBrands.Select(y => new ShiftEntitySelectDTO { Value = y.BrandID.ToString()!, Text = y.Brand!.Name }).ToList()))
            .ReverseMap()
            .ForMember(e => e.CustomFields, opt => opt.Ignore())
            .ForMember(e => e.Phone, opt => opt.Ignore())
            .ForMember(e => e.ShortPhone, opt => opt.Ignore());

        // CompanyBranch — LIST: flattened names/display-orders + the M:N projections. The scope ids —
        // CompanyId/CityId/RegionId (string) from CompanyID/CityID/RegionID (long?) — are projected by
        // convention: matching ignores case, and long? -> string is a standard conversion.
        //
        // Worth keeping in mind if anyone is tempted to Ignore them on the list: they are the targets of a LIST
        // filter (data-level access, and the Team form's branch picker sending $filter=CompanyId eq X). With no
        // scalar to bind to, EF inlines this whole collection-bearing projection into the WHERE and cannot
        // translate it. Projecting them lets EF bind the Where to e.CompanyID and push it down, leaving the
        // collections in the SELECT.
        CreateMap<CompanyBranch, CompanyBranchListDTO>()
            .ForMember(d => d.Company, opt => opt.MapFrom(e => e.Company != null ? e.Company.Name : null))
            .ForMember(d => d.Region, opt => opt.MapFrom(e => e.Region != null ? e.Region.Name : null))
            .ForMember(d => d.City, opt => opt.MapFrom(e => e.City != null ? e.City.Name : null))
            .ForMember(d => d.CompanyTerminationDate, opt => opt.MapFrom(e => e.Company != null ? e.Company.TerminationDate : null))
            .ForMember(d => d.CountryDisplayOrder, opt => opt.MapFrom(e => e.City != null && e.City.Region != null && e.City.Region.Country != null ? e.City.Region.Country.DisplayOrder : null))
            .ForMember(d => d.RegionDisplayOrder, opt => opt.MapFrom(e => e.City != null && e.City.Region != null ? e.City.Region.DisplayOrder : null))
            .ForMember(d => d.CityDisplayOrder, opt => opt.MapFrom(e => e.City != null ? e.City.DisplayOrder : null))
            .ForMember(d => d.CompanyDisplayOrder, opt => opt.MapFrom(e => e.Company != null ? e.Company.DisplayOrder : null))
            .ForMember(d => d.Brands, opt => opt.MapFrom(e => e.CompanyBranchBrands.Select(x => new ShiftEntitySelectDTO { Value = x.BrandID.ToString(), Text = x.Brand!.Name }).ToList()))
            .ForMember(d => d.Departments, opt => opt.MapFrom(e => e.CompanyBranchDepartments.Select(y => new ShiftEntitySelectDTO { Value = y.DepartmentID.ToString()!, Text = y.Department!.Name }).ToList()))
            .ForMember(d => d.Services, opt => opt.MapFrom(e => e.CompanyBranchServices.Select(y => new ShiftEntitySelectDTO { Value = y.ServiceID.ToString()!, Text = y.Service!.Name }).ToList()));

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // Company — VIEW: CustomFields with the read-side password strip; the ParentCompany select DTO (Value
        // only; Text stays null — no Include on ParentCompany). WRITE (the reverse): leave the loaded
        // CustomFields dict intact so the entity hook's password-preserving merge owns it.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<Company, CompanyDTO>()
            .ForMember(d => d.CustomFields, opt => opt.MapFrom(e => e.CustomFields == null ? null : e.CustomFields
                .ToDictionary(x => x.Key, x => new CustomFieldDTO
                {
                    DisplayName = x.Value.DisplayName,
                    IsPassword = x.Value.IsPassword,
                    IsEncrypted = x.Value.IsEncrypted,
                    Value = x.Value.IsPassword ? null : x.Value.Value,
                    HasValue = x.Value.Value != null
                })))
            .ForMember(d => d.ParentCompany, opt => opt.MapFrom(e => new ShiftEntitySelectDTO { Value = e.ParentCompanyID.ToString()! }))
            .ReverseMap()
            .ForMember(e => e.CustomFields, opt => opt.Ignore());

        // Company — LIST: the flattened parent name + the Brands aggregation. ParentCompanyID needs nothing:
        // string? on the DTO ← long? on the entity, and the names match, so the list convention binds the same
        // scalar in the same position. It therefore stays filterable — $filter=ParentCompanyID eq X still
        // translates, and EF still does not inline the Brands aggregation into the WHERE.
        CreateMap<Company, CompanyListDTO>()
            .ForMember(d => d.ParentCompanyName, opt => opt.MapFrom(e => e.ParentCompany == null ? null : e.ParentCompany.Name))
            .ForMember(d => d.Brands, opt => opt.MapFrom(e => e.CompanyBranches!
                .SelectMany(x => x.CompanyBranchBrands!)
                .Select(x => x.BrandID).Distinct()
                .Select(x => new ShiftEntitySelectDTO { Value = x.ToString() }).ToList()));

        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        // User — VIEW: the select convention can't fill CompanyBranchID (the DTO member is already named …ID,
        // so the convention appends another ID and misses — provide it explicitly); TotpEnabled; AccessTrees
        // reads through an explicit junction row, so the element convention does not reach it; Password,
        // RequireChangeAtNextLogin and SendVerification have no entity source (write-only / per-save form
        // choices that keep their defaults).
        // WRITE (the reverse): Base() maps FullName/BirthDate; the entity's upsert hook owns Username/IsActive/
        // Email/Phone/AccessTree/password/CompanyBranch-derivation/UserAccessTrees (or hands them to the staged
        // authority), so those are ignored (or customized) here.
        // ────────────────────────────────────────────────────────────────────────────────────────────────────
        CreateMap<User, UserDTO>()
            .ForMember(d => d.CompanyBranchID, opt => opt.MapFrom(e => new ShiftEntitySelectDTO { Value = e.CompanyBranchID.ToString()!, Text = e.CompanyBranch != null ? e.CompanyBranch.Name : null }))
            .ForMember(d => d.TotpEnabled, opt => opt.MapFrom(e => e.SecurityState != null ? e.SecurityState.ProtectedTotpSecret != null : e.TotpSecret != null))
            .ForMember(d => d.AccessTrees, opt => opt.MapFrom(e => e.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree.Name }).ToList()))
            .ForMember(d => d.Password, opt => opt.Ignore())
            .ForMember(d => d.RequireChangeAtNextLogin, opt => opt.Ignore())
            .ForMember(d => d.SendVerification, opt => opt.Ignore())
            .ReverseMap()
            .ForMember(e => e.IntegrationId, opt => opt.MapFrom(dto => string.IsNullOrWhiteSpace(dto.IntegrationId) ? null : dto.IntegrationId))
            .ForMember(e => e.Username, opt => opt.Ignore())
            .ForMember(e => e.IsActive, opt => opt.Ignore())
            .ForMember(e => e.Email, opt => opt.Ignore())
            .ForMember(e => e.Phone, opt => opt.Ignore())
            .ForMember(e => e.AccessTree, opt => opt.Ignore())
            .ForMember(e => e.SecurityState, opt => opt.Ignore())
            .ForMember(e => e.AccessTrees, opt => opt.Ignore()); // the M:N rows: the hook (or the authority) writes them

        // User — LIST: the flattened CompanyBranch name, TotpEnabled, LastSeen (UserLog fallback), and the
        // AccessTrees M:N projection. The scope ids CompanyBranchID/CompanyID need nothing — their names match
        // the entity's, and long? → string is a standard conversion.
        CreateMap<User, UserListDTO>()
            .ForMember(d => d.CompanyBranch, opt => opt.MapFrom(e => e.CompanyBranch != null ? e.CompanyBranch.Name : null))
            .ForMember(d => d.TotpEnabled, opt => opt.MapFrom(e => e.SecurityState != null ? e.SecurityState.ProtectedTotpSecret != null : e.TotpSecret != null))
            .ForMember(d => d.LastSeen, opt => opt.MapFrom(e => ((e.UserLog == null || e.UserLog.LastSeen == null) ? e.LastSeen : e.UserLog.LastSeen) ?? default))
            .ForMember(d => d.AccessTrees, opt => opt.MapFrom(e => e.AccessTrees.Select(y => new ShiftEntitySelectDTO { Value = y.AccessTreeID.ToString()!, Text = y.AccessTree.Name }).ToList()));
    }

    /// <summary>
    /// The automatic maps these replace do not flatten (a repository never silently reaches two levels into an
    /// entity by name alone); the maps written here keep that, so replacing one changes nothing but the members
    /// written above.
    /// </summary>
    protected override void ConfigureDefaults(MapOptions options) => options.Flattening = false;
}
