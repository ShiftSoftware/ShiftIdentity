using System;
using System.Collections.Generic;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;

/// <summary>
/// Data-source-agnostic representation of a calendar record.
/// Used as input to the calendar entry expansion algorithm.
/// </summary>
public class CalendarRecordDTO
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public CalendarEntryType EntryType { get; set; }
    public int Priority { get; set; }
    public long CompanyId { get; set; }
    public List<long> BranchIds { get; set; } = new();
    public List<CalendarShiftGroupDTO> ShiftGroups { get; set; } = new();
    public List<CalendarWeekendGroupDTO> WeekendGroups { get; set; } = new();
}

public class CalendarShiftGroupDTO
{
    public List<long> DepartmentIds { get; set; } = new();
    public List<long> BrandIds { get; set; } = new();

    /// <summary>DayOfWeek values (0=Sunday..6=Saturday). Null means all days.</summary>
    public List<int>? Days { get; set; }

    public List<CalendarShiftDTO> Shifts { get; set; } = new();
}

public class CalendarShiftDTO
{
    public string Title { get; set; } = "";
    public long StartTimeTicks { get; set; }
    public long EndTimeTicks { get; set; }
}

public class CalendarWeekendGroupDTO
{
    public List<long> DepartmentIds { get; set; } = new();
    public List<long> BrandIds { get; set; } = new();
    public List<CalendarWeekendRuleDTO> Weekends { get; set; } = new();
}

public class CalendarWeekendRuleDTO
{
    public int Day { get; set; }
    public int Repeat { get; set; }
    public int Skip { get; set; }
}
