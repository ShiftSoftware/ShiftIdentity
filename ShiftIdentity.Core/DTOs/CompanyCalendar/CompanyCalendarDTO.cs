using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.Enums;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;

public class CompanyCalendarDTO : ShiftEntityViewAndUpsertDTO
{
    public override string? ID { get; set; }

    [Required]
    public string? Title { get; set; }

    [Required]
    public DateTime? StartDate { get; set; }

    [Required]
    public DateTime? EndDate { get; set; }

    [Required]
    public CalendarEntryType? EntryType { get; set; }

    public int Priority { get; set; } = 1;

    [ShiftSoftware.ShiftEntity.Model.HashIds.CompanyHashIdConverter]
    public ShiftEntitySelectDTO? Company { get; set; }

    public DateTime? LastReplicationDate { get; set; }

    [ShiftSoftware.ShiftEntity.Model.HashIds.CompanyBranchHashIdConverter]
    public List<ShiftEntitySelectDTO> Branches { get; set; } = [];

    public List<CompanyCalendarShiftGroupDTO> ShiftGroups { get; set; } = [];
    public List<CompanyCalendarWeekendGroupDTO> WeekendGroups { get; set; } = [];
}

public class CompanyCalendarShiftGroupDTO
{
    public List<ShiftEntitySelectDTO> Departments { get; set; } = [];
    public List<ShiftEntitySelectDTO> Brands { get; set; } = [];

    /// <summary>DayOfWeek values (0=Sunday..6=Saturday). Null means all days.</summary>
    public List<int>? Days { get; set; }

    public List<CompanyCalendarShiftItemDTO> Shifts { get; set; } = [];
}

public class CompanyCalendarShiftItemDTO
{
    public string Title { get; set; } = "";
    public long StartTimeTicks { get; set; }
    public long EndTimeTicks { get; set; }

    [JsonIgnore]
    public TimeSpan? StartTime
    {
        get => StartTimeTicks > 0 ? new TimeSpan(StartTimeTicks) : null;
        set => StartTimeTicks = value?.Ticks ?? 0;
    }

    [JsonIgnore]
    public TimeSpan? EndTime
    {
        get => EndTimeTicks > 0 ? new TimeSpan(EndTimeTicks) : null;
        set => EndTimeTicks = value?.Ticks ?? 0;
    }
}

public class CompanyCalendarWeekendGroupDTO
{
    public List<ShiftEntitySelectDTO> Departments { get; set; } = [];
    public List<ShiftEntitySelectDTO> Brands { get; set; } = [];
    public List<CompanyCalendarWeekendRuleItemDTO> Weekends { get; set; } = [];
}

public class CompanyCalendarWeekendRuleItemDTO
{
    public int Day { get; set; }
    public int Repeat { get; set; } = 1;
    public int Skip { get; set; }
}
