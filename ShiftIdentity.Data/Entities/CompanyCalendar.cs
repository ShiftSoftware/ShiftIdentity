using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftEntity.Model.Replication;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung B + D): CompanyCalendar has no controller and no repository class. Base CRUD is
// attribute-driven (built-in repo + generated mapper); the Include + the non-convention mapper config (Branches
// M:N projection + the hashid-encoded Departments/Brands inside the JSON ShiftGroups/WeekendGroups children) move
// onto the entity via IConfiguresShiftRepository, and the Branches M:N reconcile via IUpsertsShiftRepository. The
// one custom endpoint (GetCalendarEvents) becomes a sibling minimal API (see MapShiftIdentityDashboard).
[TemporalShiftEntity]
[Table("CompanyCalendars", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<CompanyCalendarListDTO, CompanyCalendarDTO, ShiftIdentityActions>("api/IdentityCompanyCalendar", nameof(ShiftIdentityActions.CompanyCalendars), UseGeneratedMapper = true)]
public class CompanyCalendar :
    ShiftEntity<CompanyCalendar>,
    IEntityHasCompany<CompanyCalendar>,
    IShiftEntityReplication,
    IConfiguresShiftRepository<CompanyCalendar, CompanyCalendarListDTO, CompanyCalendarDTO>,
    IUpsertsShiftRepository<CompanyCalendar, CompanyCalendarListDTO, CompanyCalendarDTO>
{
    /// <inheritdoc />
    public DateTimeOffset? LastReplicationDate { get; set; }

    /// <inheritdoc />
    public string? LastReplicationStamp { get; set; }

    public string Title { get; set; } = default!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public CalendarEntryType EntryType { get; set; }
    public int Priority { get; set; }
    public long? CompanyID { get; set; }

    /// <summary>
    /// Shift configuration groups, stored as a JSON column.
    /// For Workdays: department/brand/day-scoped shift definitions.
    /// For Holidays: a synthetic single shift with Title = holiday name and zero times.
    /// </summary>
    public List<CompanyCalendarShiftGroup> ShiftGroups { get; set; } = new();

    /// <summary>
    /// Weekend rule groups, stored as a JSON column.
    /// Only used for Workday entries.
    /// </summary>
    public List<CompanyCalendarWeekendGroup> WeekendGroups { get; set; } = new();

    public ICollection<CompanyCalendarBranch> Branches { get; set; } = new HashSet<CompanyCalendarBranch>();

    public void ConfigureRepository(ShiftRepositoryConfigurationContext<CompanyCalendar, CompanyCalendarListDTO, CompanyCalendarDTO> context)
    {
        context.Options.IncludeRelatedEntitiesWithFindAsync(i => i.Include(e => e.Branches));

        context.Options.UseGeneratedMapper(map => map
            // VIEW — Branches M:N join → List<ShiftEntitySelectDTO> (raw ids; the DTO's CompanyBranchHashIdConverter
            // encodes on the wire). The write side is owned by the IUpsertsShiftRepository merge, so ignore it here.
            .ForView(v => v.Branches, e => e.Branches
                .Select(b => new ShiftEntitySelectDTO { Value = b.CompanyBranchID.ToString() }).ToList())
            .IgnoreEntity(e => e.Branches)

            // JSON children: auto-deep composes the structure + trivial grandchildren, but the nested
            // Departments/Brands are hashid-encoded long lists — supply them explicitly on the pair mappers.
            .ForViewChildren(v => v.ShiftGroups, e => e.ShiftGroups, child => child
                .For(d => d.Departments, (sg, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return sg.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = h.Encode<DepartmentListDTO>(id) }).ToList(); })
                .For(d => d.Brands, (sg, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return sg.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = h.Encode<BrandListDTO>(id) }).ToList(); }))
            .ForEntityChildren(e => e.ShiftGroups, dto => dto.ShiftGroups, child => child
                .For(sg => sg.DepartmentIds, (d, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return d.Departments.Where(x => x.Value != null).Select(x => h.Decode<DepartmentListDTO>(x.Value!)).ToList(); })
                .For(sg => sg.BrandIds, (d, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return d.Brands.Where(x => x.Value != null).Select(x => h.Decode<BrandListDTO>(x.Value!)).ToList(); }))

            .ForViewChildren(v => v.WeekendGroups, e => e.WeekendGroups, child => child
                .For(d => d.Departments, (wg, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return wg.DepartmentIds.Select(id => new ShiftEntitySelectDTO { Value = h.Encode<DepartmentListDTO>(id) }).ToList(); })
                .For(d => d.Brands, (wg, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return wg.BrandIds.Select(id => new ShiftEntitySelectDTO { Value = h.Encode<BrandListDTO>(id) }).ToList(); }))
            .ForEntityChildren(e => e.WeekendGroups, dto => dto.WeekendGroups, child => child
                .For(wg => wg.DepartmentIds, (d, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return d.Departments.Where(x => x.Value != null).Select(x => h.Decode<DepartmentListDTO>(x.Value!)).ToList(); })
                .For(wg => wg.BrandIds, (d, c) => { var h = c.Services!.GetRequiredService<IHashIdService>(); return d.Brands.Where(x => x.Value != null).Select(x => h.Decode<BrandListDTO>(x.Value!)).ToList(); })));
    }

    public async ValueTask<CompanyCalendar> UpsertAsync(
        CompanyCalendar entity,
        CompanyCalendarDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<CompanyCalendar, CompanyCalendarListDTO, CompanyCalendarDTO> context)
    {
        // Base() maps scalars + JSON children + the Company FK (Branches is IgnoreEntity'd), audit-stamps, and runs
        // the company-scoped data-level write check. Then reconcile the Branches M:N join rows (merge-by-key, not
        // replace-with-new) — ported from the old AutoMapper AfterMap. On update the existing rows are loaded via
        // the Include above; on insert the collection starts empty and all are added.
        var result = await context.Base();

        result.Branches ??= new HashSet<CompanyCalendarBranch>();

        var toRemove = result.Branches
            .Where(b => !dto.Branches.Any(s => s.Value == b.CompanyBranchID.ToString()))
            .ToList();
        foreach (var b in toRemove)
            result.Branches.Remove(b);

        foreach (var branchDto in dto.Branches)
            if (!result.Branches.Any(b => b.CompanyBranchID.ToString() == branchDto.Value))
                result.Branches.Add(new CompanyCalendarBranch { CompanyBranchID = branchDto.Value.ToLong() });

        return result;
    }
}

#region JSON column owned types

public class CompanyCalendarShiftGroup
{
    public List<long> DepartmentIds { get; set; } = new();
    public List<long> BrandIds { get; set; } = new();

    /// <summary>DayOfWeek values (0=Sunday..6=Saturday). Null means all days.</summary>
    public List<int>? Days { get; set; }

    public List<CompanyCalendarShift> Shifts { get; set; } = new();
}

public class CompanyCalendarShift
{
    public string Title { get; set; } = default!;
    public long StartTimeTicks { get; set; }
    public long EndTimeTicks { get; set; }
}

public class CompanyCalendarWeekendGroup
{
    public List<long> DepartmentIds { get; set; } = new();
    public List<long> BrandIds { get; set; } = new();
    public List<CompanyCalendarWeekendRule> Weekends { get; set; } = new();
}

public class CompanyCalendarWeekendRule
{
    public int Day { get; set; }
    public int Repeat { get; set; }
    public int Skip { get; set; }
}

#endregion
