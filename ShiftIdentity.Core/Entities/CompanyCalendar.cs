using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Flags;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.Entities;

[TemporalShiftEntity]
[Table("CompanyCalendars", Schema = "ShiftIdentity")]
public class CompanyCalendar :
    ShiftEntity<CompanyCalendar>,
    IEntityHasCompany<CompanyCalendar>
{
    public string Title { get; set; } = default!;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public CalendarEntryType EntryType { get; set; }
    public int Priority { get; set; }
    public long? CompanyID { get; set; }
    public new DateTime? LastReplicationDate { get; set; }

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
