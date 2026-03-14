using System;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;

/// <summary>
/// A single computed calendar entry produced by expanding calendar records day-by-day.
/// </summary>
public class CalendarEntryDTO
{
    public long CalendarId { get; set; }
    public string CalendarTitle { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public CalendarEntryStatus Status { get; set; }
    public long CompanyId { get; set; }
    public long BranchId { get; set; }
    public long DepartmentId { get; set; }
    public int Priority { get; set; }
    public CalendarEntryType EntryType { get; set; }

    public string Title => Status switch
    {
        CalendarEntryStatus.WorkPeriod or CalendarEntryStatus.InactiveWorkPeriod
            => $"{StartDate:hh:mm tt} - {EndDate:hh:mm tt}",
        CalendarEntryStatus.Weekend => "Weekend",
        CalendarEntryStatus.Holiday => CalendarTitle,
        _ => "",
    };
}
