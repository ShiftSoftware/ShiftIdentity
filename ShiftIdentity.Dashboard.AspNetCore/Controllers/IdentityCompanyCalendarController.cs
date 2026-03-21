using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model.HashIds;
using ShiftSoftware.ShiftEntity.Web;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Brand;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Company;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyBranch;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.DTOs.Department;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.ShiftIdentity.Data.Repositories;
using ShiftSoftware.ShiftIdentity.Data.Services;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Controllers;

[Route("api/[controller]")]
public class IdentityCompanyCalendarController : ShiftEntitySecureControllerAsync<CompanyCalendarRepository, CompanyCalendar, CompanyCalendarListDTO, CompanyCalendarDTO>
{
    private readonly ShiftIdentityDbContext _db;
    private readonly CalendarService _calendarService;

    public IdentityCompanyCalendarController(
        ShiftIdentityDbContext db,
        CalendarService calendarService)
        : base(ShiftIdentityActions.CompanyCalendars)
    {
        _db = db;
        _calendarService = calendarService;
    }

    [HttpPost("GetCalendarEvents")]
    public async Task<IActionResult> GetCalendarEvents([FromBody] CalendarFilterDTO filter)
    {
        DecodeFilterHashIds(filter);

        var records = await GetCalendarRecords(filter);

        var entries = _calendarService.GetCalendarEntries(records, filter);
        var events = _calendarService.FormatCalendarEvents(entries);

        return Ok(events);
    }

    private static void DecodeFilterHashIds(CalendarFilterDTO filter)
    {
        if (!string.IsNullOrEmpty(filter.CompanyHashId))
            filter.CompanyId = ShiftEntityHashIdService.Decode<CompanyListDTO>(filter.CompanyHashId);

        if (filter.ViewBranchHashIds is { Count: > 0 })
            filter.ViewBranchIds = filter.ViewBranchHashIds
                .Select(ShiftEntityHashIdService.Decode<CompanyBranchListDTO>)
                .ToList();

        if (filter.ViewDepartmentHashIds is { Count: > 0 })
            filter.ViewDepartmentIds = filter.ViewDepartmentHashIds
                .Select(ShiftEntityHashIdService.Decode<DepartmentListDTO>)
                .ToList();

        if (filter.ViewBrandHashIds is { Count: > 0 })
            filter.ViewBrandIds = filter.ViewBrandHashIds
                .Select(ShiftEntityHashIdService.Decode<BrandListDTO>)
                .ToList();
    }

    private async Task<List<CalendarRecordDTO>> GetCalendarRecords(CalendarFilterDTO filter)
    {
        var calendars = await _db.CompanyCalendars
            .Include(c => c.Branches)
            .Where(x => !x.IsDeleted)
            .Where(x => x.StartDate <= filter.ViewEndDate)
            .Where(x => filter.ViewStartDate <= x.EndDate)
            .Where(x => filter.CompanyId == null || x.CompanyID == filter.CompanyId)
            .AsNoTracking()
            .ToListAsync();

        return calendars.Select(MapToRecord).ToList();
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
