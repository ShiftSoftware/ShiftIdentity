using System;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;

public class CompanyCalendarListDTO : ShiftEntityListDTO
{
    public override string? ID { get; set; }
    public string Title { get; set; } = "";
    public CalendarEntryType EntryType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int Priority { get; set; }

    [ShiftSoftware.ShiftEntity.Model.HashIds.CompanyHashIdConverter]
    public string? CompanyID { get; set; }
}
