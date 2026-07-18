using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Services;
using System.Collections.Generic;
using System.Linq;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Endpoints;

// The CompanyCalendar feature's CUSTOM (non-CRUD) endpoints. Its CRUD is attribute-driven (on the CompanyCalendar
// entity); this is the sibling minimal API for what used to be IdentityCompanyCalendarController.GetCalendarEvents.
// Composed into the dashboard by MapShiftIdentityDashboard(). Convention: each feature keeps its custom endpoints
// in its own *Endpoints class here, so it's obvious where a route lives.
internal static class CompanyCalendarEndpoints
{
    public static IEndpointRouteBuilder MapCompanyCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        // Ported verbatim from IdentityCompanyCalendarController.GetCalendarEvents (route + verb byte-identical).
        // NOTE: the old controller action carried no explicit per-action TypeAuth check (only the ambient
        // RequireAuthorization), so this preserves authenticated-only access. To gate it by the CompanyCalendars
        // Read action instead, append .RequireTypeAuthRead(ShiftIdentityActions.CompanyCalendars).
        app.MapPost("api/IdentityCompanyCalendar/GetCalendarEvents",
            async (CalendarFilterDTO filter,
                   ShiftIdentityDbContext db,
                   CalendarService calendarService,
                   IHashIdService hashIdService) =>
            {
                if (!string.IsNullOrEmpty(filter.CompanyHashId))
                    filter.CompanyId = hashIdService.Decode<CompanyListDTO>(filter.CompanyHashId);

                if (filter.ViewBranchHashIds is { Count: > 0 })
                    filter.ViewBranchIds = filter.ViewBranchHashIds
                        .Select(hashIdService.Decode<CompanyBranchListDTO>)
                        .ToList();

                if (filter.ViewDepartmentHashIds is { Count: > 0 })
                    filter.ViewDepartmentIds = filter.ViewDepartmentHashIds
                        .Select(hashIdService.Decode<DepartmentListDTO>)
                        .ToList();

                if (filter.ViewBrandHashIds is { Count: > 0 })
                    filter.ViewBrandIds = filter.ViewBrandHashIds
                        .Select(hashIdService.Decode<BrandListDTO>)
                        .ToList();

                var calendars = await db.CompanyCalendars
                    .Include(c => c.Branches)
                    .Where(x => !x.IsDeleted)
                    .Where(x => x.StartDate <= filter.ViewEndDate)
                    .Where(x => filter.ViewStartDate <= x.EndDate)
                    .Where(x => filter.CompanyId == null || x.CompanyID == filter.CompanyId)
                    .AsNoTracking()
                    .ToListAsync();

                var records = calendars.Select(MapToRecord).ToList();

                var entries = calendarService.GetCalendarEntries(records, filter);
                var events = calendarService.FormatCalendarEvents(entries);

                return Results.Ok(events);
            })
            .RequireAuthorization();

        return app;
    }

    private static CalendarRecordDTO MapToRecord(CompanyCalendar c) => new()
    {
        Id = c.ID,
        Title = c.Title,
        StartDate = c.StartDate,
        EndDate = c.EndDate,
        EntryType = c.EntryType,
        Priority = c.Priority,
        CompanyId = c.CompanyID ?? 0,
        BranchIds = c.Branches.Select(b => b.CompanyBranchID).ToList(),
        ShiftGroups = c.ShiftGroups.Select(sg => new CalendarShiftGroupDTO
        {
            DepartmentIds = sg.DepartmentIds,
            BrandIds = sg.BrandIds,
            Days = sg.Days,
            Shifts = sg.Shifts.Select(s => new CalendarShiftDTO
            {
                Title = s.Title,
                StartTimeTicks = s.StartTimeTicks,
                EndTimeTicks = s.EndTimeTicks,
            }).ToList(),
        }).ToList(),
        WeekendGroups = c.WeekendGroups.Select(wg => new CalendarWeekendGroupDTO
        {
            DepartmentIds = wg.DepartmentIds,
            BrandIds = wg.BrandIds,
            Weekends = wg.Weekends.Select(w => new CalendarWeekendRuleDTO
            {
                Day = w.Day,
                Repeat = w.Repeat,
                Skip = w.Skip,
            }).ToList(),
        }).ToList(),
    };
}
