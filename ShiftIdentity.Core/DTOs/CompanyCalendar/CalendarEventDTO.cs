namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;

/// <summary>
/// FullCalendar-compatible event object returned by the calendar events API.
/// </summary>
public class CalendarEventDTO
{
    public string Title { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public bool AllDay { get; set; } = true;
    public string ClassNames { get; set; } = "";
    public long Id { get; set; }
    public CalendarEventExtendedPropsDTO ExtendedProps { get; set; } = new();
    public int Priority { get; set; }
    public int EntryType { get; set; }
    public int Status { get; set; }
}

public class CalendarEventExtendedPropsDTO
{
    public string CalendarTitle { get; set; } = "";
}
