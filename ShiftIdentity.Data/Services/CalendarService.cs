using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Data.Services;

/// <summary>
/// Core calendar business logic: expands stored calendar records into individual
/// day-by-day entries, applies priority resolution, and formats for FullCalendar.
/// </summary>
public class CalendarService
{
    private static readonly Dictionary<CalendarEntryStatus, string> StatusCssClasses = new()
    {
        { CalendarEntryStatus.WorkPeriod, "work-event" },
        { CalendarEntryStatus.Weekend, "weekend-event" },
        { CalendarEntryStatus.Holiday, "holiday-event" },
        { CalendarEntryStatus.InactiveWorkPeriod, "inactive-event" },
    };

    public List<CalendarEntryDTO> GetCalendarEntries(
        List<CalendarRecordDTO> calendars,
        CalendarFilterDTO filter)
    {
        var entries = new List<CalendarEntryDTO>();

        foreach (var calendar in calendars)
        {
            var weekendCursors = new List<WeekendCursor>();

            var dateCursor = calendar.StartDate > filter.ViewStartDate
                ? calendar.StartDate
                : filter.ViewStartDate;

            var endBound = calendar.EndDate > filter.ViewEndDate
                ? filter.ViewEndDate.AddDays(-1)
                : calendar.EndDate;

            while (dateCursor <= endBound)
            {
                foreach (var branchId in calendar.BranchIds)
                {
                    if (filter.ViewBranchIds is { Count: > 0 } && !filter.ViewBranchIds.Contains(branchId))
                        continue;

                    ExpandShifts(entries, calendar, filter, dateCursor, branchId);
                    ExpandWeekends(entries, calendar, filter, dateCursor, branchId, weekendCursors);
                }

                dateCursor = dateCursor.AddDays(1);
            }
        }

        ResolvePriorities(entries);

        entries = entries
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.StartDate)
            .ToList();

        return entries;
    }

    public List<CalendarEventDTO> FormatCalendarEvents(List<CalendarEntryDTO> entries)
    {
        var deduplicated = entries
            .GroupBy(x => new { x.CalendarId, StartDate = x.StartDate, EndDate = x.EndDate, x.Status })
            .Select(g => g.First());

        return deduplicated.Select(x => new CalendarEventDTO
        {
            Title = x.Title,
            Start = x.StartDate.ToString("yyyy-MM-dd HH:mm:ss"),
            End = x.EndDate.ToString("yyyy-MM-dd HH:mm:ss"),
            AllDay = true,
            ClassNames = StatusCssClasses.GetValueOrDefault(x.Status, ""),
            Id = x.CalendarId,
            ExtendedProps = new CalendarEventExtendedPropsDTO
            {
                CalendarTitle = x.CalendarTitle,
            },
            Priority = x.Priority,
            EntryType = (int)x.EntryType,
            Status = (int)x.Status,
        }).ToList();
    }

    public object GetCalendarEventsResponse(
        List<CalendarRecordDTO> calendars,
        CalendarFilterDTO filter)
    {
        var entries = GetCalendarEntries(calendars, filter);
        return new
        {
            status = "ok",
            events = FormatCalendarEvents(entries),
        };
    }

    #region Expansion

    private static void ExpandShifts(
        List<CalendarEntryDTO> entries,
        CalendarRecordDTO calendar,
        CalendarFilterDTO filter,
        DateTime dateCursor,
        long branchId)
    {
        foreach (var shiftGroup in calendar.ShiftGroups)
        {
            if (shiftGroup.Days != null && !shiftGroup.Days.Contains((int)dateCursor.DayOfWeek))
                continue;

            var deptIds = shiftGroup.DepartmentIds is { Count: > 0 }
                ? shiftGroup.DepartmentIds
                : new List<long> { 0 };

            foreach (var deptId in deptIds)
            {
                if (filter.ViewDepartmentIds is { Count: > 0 } && !filter.ViewDepartmentIds.Contains(deptId))
                    continue;

                foreach (var shift in shiftGroup.Shifts)
                {
                    var startTime = TimeSpan.FromTicks(shift.StartTimeTicks);
                    var endTime = TimeSpan.FromTicks(shift.EndTimeTicks);

                    entries.Add(new CalendarEntryDTO
                    {
                        CalendarId = calendar.Id,
                        CalendarTitle = calendar.Title,
                        StartDate = new DateTime(dateCursor.Year, dateCursor.Month, dateCursor.Day,
                            startTime.Hours, startTime.Minutes, startTime.Seconds),
                        EndDate = new DateTime(dateCursor.Year, dateCursor.Month, dateCursor.Day,
                            endTime.Hours, endTime.Minutes, endTime.Seconds),
                        Status = calendar.EntryType == CalendarEntryType.Holiday
                            ? CalendarEntryStatus.Holiday
                            : CalendarEntryStatus.WorkPeriod,
                        CompanyId = calendar.CompanyId,
                        BranchId = branchId,
                        DepartmentId = deptId,
                        Priority = calendar.Priority,
                        EntryType = calendar.EntryType,
                    });
                }
            }
        }
    }

    private static void ExpandWeekends(
        List<CalendarEntryDTO> entries,
        CalendarRecordDTO calendar,
        CalendarFilterDTO filter,
        DateTime dateCursor,
        long branchId,
        List<WeekendCursor> weekendCursors)
    {
        foreach (var weekendGroup in calendar.WeekendGroups)
        {
            var deptIds = weekendGroup.DepartmentIds is { Count: > 0 }
                ? weekendGroup.DepartmentIds
                : new List<long> { 0 };

            foreach (var deptId in deptIds)
            {
                if (filter.ViewDepartmentIds is { Count: > 0 } && !filter.ViewDepartmentIds.Contains(deptId))
                    continue;

                foreach (var rule in weekendGroup.Weekends)
                {
                    if ((int)dateCursor.DayOfWeek != rule.Day)
                        continue;

                    var cursor = weekendCursors.FirstOrDefault(c =>
                        c.Branch == branchId &&
                        c.Department == deptId &&
                        c.Day == rule.Day);

                    if (cursor == null)
                    {
                        var initialCount = 0;

                        if (calendar.StartDate < filter.ViewStartDate)
                            initialCount = CountDays(dateCursor.DayOfWeek, calendar.StartDate, filter.ViewStartDate);

                        if (dateCursor.DayOfWeek == filter.ViewStartDate.DayOfWeek)
                            initialCount--;

                        cursor = new WeekendCursor
                        {
                            Branch = branchId,
                            Department = deptId,
                            Day = rule.Day,
                            Count = initialCount + rule.Skip,
                        };

                        weekendCursors.Add(cursor);
                    }

                    var weekendCount = cursor.Count;
                    cursor.Count++;

                    if (weekendCount % rule.Repeat != 0)
                        continue;

                    entries.Add(new CalendarEntryDTO
                    {
                        CalendarId = calendar.Id,
                        CalendarTitle = calendar.Title,
                        StartDate = dateCursor,
                        EndDate = dateCursor,
                        Status = CalendarEntryStatus.Weekend,
                        CompanyId = calendar.CompanyId,
                        BranchId = branchId,
                        DepartmentId = deptId,
                        Priority = calendar.Priority,
                        EntryType = calendar.EntryType,
                    });
                }
            }
        }
    }

    #endregion

    #region Priority Resolution

    private static void ResolvePriorities(List<CalendarEntryDTO> entries)
    {
        foreach (var dayGroup in entries.GroupBy(x => x.StartDate.Date))
        {
            foreach (var companyGroup in dayGroup.GroupBy(x => x.CompanyId))
            {
                foreach (var branchGroup in companyGroup.GroupBy(x => x.BranchId))
                {
                    foreach (var deptGroup in branchGroup.GroupBy(x => x.DepartmentId))
                    {
                        var groupEntries = deptGroup.ToList();

                        foreach (var entry in groupEntries)
                        {
                            if (entry.Status != CalendarEntryStatus.WorkPeriod)
                                continue;

                            bool suppressed =
                                groupEntries.Any(x =>
                                    (x.Status == CalendarEntryStatus.Holiday && x.Priority >= entry.Priority) ||
                                    (x.Status == CalendarEntryStatus.WorkPeriod && x.Priority > entry.Priority)) ||
                                groupEntries.Any(x =>
                                    x.Status == CalendarEntryStatus.Weekend && x.Priority >= entry.Priority);

                            if (suppressed)
                                entry.Status = CalendarEntryStatus.InactiveWorkPeriod;
                        }
                    }
                }
            }
        }
    }

    #endregion

    #region Helpers

    public static int CountDays(DayOfWeek day, DateTime start, DateTime end)
    {
        TimeSpan ts = end - start;
        int count = (int)Math.Floor(ts.TotalDays / 7);
        int remainder = (int)(ts.TotalDays % 7);
        int sinceLastDay = (int)(end.DayOfWeek - day);
        if (sinceLastDay < 0) sinceLastDay += 7;

        if (remainder >= sinceLastDay) count++;

        return count;
    }

    private class WeekendCursor
    {
        public long Branch { get; set; }
        public long Department { get; set; }
        public int Day { get; set; }
        public int Count { get; set; }
    }

    #endregion
}
