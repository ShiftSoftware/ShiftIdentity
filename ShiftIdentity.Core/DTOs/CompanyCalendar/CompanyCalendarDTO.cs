using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json.Serialization;
using FluentValidation;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Localization;

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

    // Set once the form picks a time for this line, so a picked 00:00 shows even while the other time is
    // still 0 ticks. It is not sent or stored.
    private bool timePicked;

    /// <summary>
    /// The start as a time of day, for the form. 00:00 is stored as 0 ticks. While both times are 0 ticks the line
    /// has no times yet (the line a holiday group carries looks like this), so both read as null until a time is
    /// picked.
    /// </summary>
    [JsonIgnore]
    public TimeSpan? StartTime
    {
        get => HasTimes ? new TimeSpan(StartTimeTicks) : null;
        set
        {
            StartTimeTicks = value?.Ticks ?? 0;
            timePicked |= value is not null;
        }
    }

    /// <summary>The end as a time of day, for the form. See <see cref="StartTime"/>.</summary>
    [JsonIgnore]
    public TimeSpan? EndTime
    {
        get => HasTimes ? new TimeSpan(EndTimeTicks) : null;
        set
        {
            EndTimeTicks = value?.Ticks ?? 0;
            timePicked |= value is not null;
        }
    }

    /// <summary>Whether the line has times: a time was picked in the form, or either stored time is not 0 ticks.</summary>
    [JsonIgnore]
    public bool HasTimes => timePicked || StartTimeTicks != 0 || EndTimeTicks != 0;

    /// <summary>
    /// Whether the shift ends on the next day. An end before the start means that, for example 22:00 to 06:00, or
    /// 16:00 to 00:00 (midnight).
    /// </summary>
    [JsonIgnore]
    public bool EndsNextDay => EndTimeTicks < StartTimeTicks;
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

/// <summary>
/// The rules a calendar entry must meet to work. The dashboard form runs it, and so does the api/IdentityCompanyCalendar
/// save (in the entity's upsert hook), so API clients get the same messages.
/// <para>
/// Days = null is valid and means "every day". Stored entries and API clients rely on that. An empty list means "no
/// day", so it is rejected.
/// </para>
/// <para>
/// A holiday's shift groups limit the holiday to some departments and brands. Each such group carries one line
/// with no times, so the shift time rules apply to workday entries only.
/// </para>
/// </summary>
public class CompanyCalendarValidator : AbstractValidator<CompanyCalendarDTO>
{
    public CompanyCalendarValidator(ShiftIdentityLocalizer localizer)
    {
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .When(x => x.StartDate is not null && x.EndDate is not null)
            .WithMessage(localizer["The end date cannot be before the start date"]);

        RuleFor(x => x.Priority)
            .GreaterThanOrEqualTo(0)
            .WithMessage(localizer["The priority cannot be negative"]);

        RuleFor(x => x.Company)
            .Must(company => !string.IsNullOrWhiteSpace(company?.Value))
            .WithMessage(localizer["Pick a company"]);

        // An entry with no branch never applies.
        RuleFor(x => x.Branches)
            .NotEmpty()
            .WithMessage(localizer["Pick at least one branch"]);

        // A workday entry works only through its shift groups.
        RuleFor(x => x.ShiftGroups)
            .NotEmpty()
            .When(x => x.EntryType == CalendarEntryType.Workday)
            .WithMessage(localizer["Add at least one shift group"]);

        // Every shift group, on workdays and holidays:
        // - It names at least one department and one brand. CalendarService reads an empty list as "all", but the
        //   calendar page (which always filters by one department) and the readers of the replicated calendar
        //   documents read it as "none". So an empty list is not allowed.
        // - Days is null ("every day"), or it names at least one day, each from 0 to 6 and each only once.
        // - It has at least one line. A group applies only through its lines.
        RuleForEach(x => x.ShiftGroups).ChildRules(group =>
        {
            group.RuleFor(g => g.Departments)
                .NotEmpty()
                .WithMessage(localizer["Pick at least one department"]);

            group.RuleFor(g => g.Brands)
                .NotEmpty()
                .WithMessage(localizer["Pick at least one brand"]);

            group.RuleFor(g => g.Days)
                .Must(days => days is null || days.Count > 0)
                .WithMessage(localizer["Pick at least one day"])
                .Must(days => days is null || days.All(day => day is >= 0 and <= 6))
                .WithMessage(localizer["A day must be between 0 (Sunday) and 6 (Saturday)"])
                .Must(days => days is null || days.Distinct().Count() == days.Count)
                .WithMessage(localizer["A day can be listed only once"]);

            group.RuleFor(g => g.Shifts)
                .NotEmpty()
                .WithMessage(localizer["Add at least one shift"]);
        });

        // Workday shift times. 00:00 is 0 ticks, so a line whose start and end are both 0 ticks has no times yet.
        // An end before the start means the shift ends on the next day (22:00 to 06:00, or 16:00 to 00:00).
        RuleForEach(x => x.ShiftGroups).ChildRules(group =>
        {
            group.RuleForEach(g => g.Shifts).ChildRules(shift =>
            {
                shift.RuleFor(s => s.StartTimeTicks)
                    .Must((s, start) => start != 0 || s.EndTimeTicks != 0)
                    .WithMessage(localizer["Set the start and end times"])
                    .InclusiveBetween(0, TimeSpan.TicksPerDay - 1)
                    .WithMessage(localizer["Enter a time of day"]);

                shift.RuleFor(s => s.EndTimeTicks)
                    .InclusiveBetween(0, TimeSpan.TicksPerDay - 1)
                    .WithMessage(localizer["Enter a time of day"])
                    .Must((s, end) => end == 0 || end != s.StartTimeTicks)
                    .WithMessage(localizer["The end time must be different from the start time"]);
            });
        }).When(x => x.EntryType == CalendarEntryType.Workday);

        // Weekend groups name departments and brands for the same reason as shift groups. A rule that repeats
        // every 0 weeks would make the calendar fail with a division by zero.
        RuleForEach(x => x.WeekendGroups).ChildRules(group =>
        {
            group.RuleFor(g => g.Departments)
                .NotEmpty()
                .WithMessage(localizer["Pick at least one department"]);

            group.RuleFor(g => g.Brands)
                .NotEmpty()
                .WithMessage(localizer["Pick at least one brand"]);

            group.RuleFor(g => g.Weekends)
                .NotEmpty()
                .WithMessage(localizer["Add at least one weekend rule"]);

            group.RuleForEach(g => g.Weekends).ChildRules(rule =>
            {
                rule.RuleFor(r => r.Day)
                    .InclusiveBetween(0, 6)
                    .WithMessage(localizer["A day must be between 0 (Sunday) and 6 (Saturday)"]);

                rule.RuleFor(r => r.Repeat)
                    .GreaterThanOrEqualTo(1)
                    .WithMessage(localizer["The number of weeks must be at least 1"]);

                rule.RuleFor(r => r.Skip)
                    .GreaterThanOrEqualTo(0)
                    .WithMessage(localizer["The number of skipped cycles cannot be negative"]);
            });
        });
    }
}
