using System.Globalization;
using FluentValidation;
using FluentValidation.Internal;
using FluentValidation.Results;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The calendar entry rules. Days = null means "every day" and stays valid. A holiday group carries one line with
/// no times, the way stored holidays do, so it must pass. Every message names the property path of the line it
/// belongs to, which is how the form shows it on that line and how an API client finds it.
/// </summary>
public class CompanyCalendarValidatorTests
{
    private const long Hour = TimeSpan.TicksPerHour;

    [Fact]
    public void A_holiday_limited_to_some_departments_and_brands_is_valid_with_days_null_and_a_line_with_no_times()
    {
        Assert.Empty(Validate(Holiday(ScopeGroup())).Errors);
    }

    [Fact]
    public void A_holiday_with_no_group_is_valid_and_applies_to_the_whole_branch()
    {
        Assert.Empty(Validate(Holiday()).Errors);
    }

    [Fact]
    public void A_workday_group_that_applies_every_day_is_valid()
    {
        Assert.Empty(Validate(Workday(WorkGroup(days: null))).Errors);
    }

    [Theory]
    [InlineData(0, 8)]      // starts at midnight
    [InlineData(16, 0)]     // ends at midnight
    [InlineData(22, 6)]     // ends the next day
    [InlineData(8, 16)]
    public void A_workday_shift_is_valid_when_its_times_differ(int startHour, int endHour)
    {
        var entry = Workday(WorkGroup(shift: Shift(startHour * Hour, endHour * Hour)));

        Assert.Empty(Validate(entry).Errors);
    }

    [Fact]
    public void A_workday_needs_a_shift_group_even_when_it_has_weekend_groups()
    {
        var entry = Workday();
        entry.WeekendGroups = [WeekendGroup()];

        AssertOnly(Validate(entry), ("ShiftGroups", "Add at least one shift group"));
    }

    [Fact]
    public void A_workday_group_needs_departments_brands_days_and_a_line()
    {
        AssertOnly(Validate(Workday(new CompanyCalendarShiftGroupDTO { Days = [] })),
            ("ShiftGroups[0].Departments", "Pick at least one department"),
            ("ShiftGroups[0].Brands", "Pick at least one brand"),
            ("ShiftGroups[0].Days", "Pick at least one day"),
            ("ShiftGroups[0].Shifts", "Add at least one shift"));
    }

    [Fact]
    public void A_holiday_group_needs_departments_brands_days_and_its_line()
    {
        AssertOnly(Validate(Holiday(new CompanyCalendarShiftGroupDTO { Days = [] })),
            ("ShiftGroups[0].Departments", "Pick at least one department"),
            ("ShiftGroups[0].Brands", "Pick at least one brand"),
            ("ShiftGroups[0].Days", "Pick at least one day"),
            ("ShiftGroups[0].Shifts", "Add at least one shift"));
    }

    [Theory]
    [InlineData(new[] { 7 }, "A day must be between 0 (Sunday) and 6 (Saturday)")]
    [InlineData(new[] { -1 }, "A day must be between 0 (Sunday) and 6 (Saturday)")]
    [InlineData(new[] { 1, 1 }, "A day can be listed only once")]
    public void Days_are_weekday_numbers_listed_once(int[] days, string message)
    {
        AssertOnly(Validate(Workday(WorkGroup(days: [.. days]))), ("ShiftGroups[0].Days", message));
    }

    [Fact]
    public void A_workday_line_with_no_times_asks_for_them()
    {
        AssertOnly(Validate(Workday(WorkGroup(shift: Shift(0, 0)))),
            ("ShiftGroups[0].Shifts[0].StartTimeTicks", "Set the start and end times"));
    }

    [Fact]
    public void A_workday_line_that_ends_when_it_starts_is_rejected()
    {
        AssertOnly(Validate(Workday(WorkGroup(shift: Shift(8 * Hour, 8 * Hour)))),
            ("ShiftGroups[0].Shifts[0].EndTimeTicks", "The end time must be different from the start time"));
    }

    [Fact]
    public void A_time_is_a_time_of_day()
    {
        AssertOnly(Validate(Workday(WorkGroup(shift: Shift(-1, TimeSpan.TicksPerDay)))),
            ("ShiftGroups[0].Shifts[0].StartTimeTicks", "Enter a time of day"),
            ("ShiftGroups[0].Shifts[0].EndTimeTicks", "Enter a time of day"));
    }

    [Fact]
    public void A_weekend_group_needs_departments_brands_and_a_rule()
    {
        var entry = Workday(WorkGroup());
        entry.WeekendGroups = [new CompanyCalendarWeekendGroupDTO()];

        AssertOnly(Validate(entry),
            ("WeekendGroups[0].Departments", "Pick at least one department"),
            ("WeekendGroups[0].Brands", "Pick at least one brand"),
            ("WeekendGroups[0].Weekends", "Add at least one weekend rule"));
    }

    [Fact]
    public void A_weekend_rule_repeats_every_week_or_more_and_skips_zero_or_more()
    {
        var entry = Workday(WorkGroup());
        entry.WeekendGroups = [WeekendGroup(new CompanyCalendarWeekendRuleItemDTO { Day = 7, Repeat = 0, Skip = -1 })];

        AssertOnly(Validate(entry),
            ("WeekendGroups[0].Weekends[0].Day", "A day must be between 0 (Sunday) and 6 (Saturday)"),
            ("WeekendGroups[0].Weekends[0].Repeat", "The number of weeks must be at least 1"),
            ("WeekendGroups[0].Weekends[0].Skip", "The number of skipped cycles cannot be negative"));
    }

    [Fact]
    public void An_entry_needs_a_company_a_branch_a_date_range_and_a_priority_of_zero_or_more()
    {
        var entry = Holiday();
        entry.Company = null;
        entry.Branches = [];
        entry.EndDate = entry.StartDate!.Value.AddDays(-1);
        entry.Priority = -1;

        AssertOnly(Validate(entry),
            ("EndDate", "The end date cannot be before the start date"),
            ("Priority", "The priority cannot be negative"),
            ("Company", "Pick a company"),
            ("Branches", "Pick at least one branch"));
    }

    [Fact]
    public void Lists_sent_as_null_are_rejected_rather_than_stored()
    {
        var group = WorkGroup();
        group.Departments = null!;
        group.Shifts = null!;
        var entry = Workday(group);
        entry.Branches = null!;

        AssertOnly(Validate(entry),
            ("Branches", "Pick at least one branch"),
            ("ShiftGroups[0].Departments", "Pick at least one department"),
            ("ShiftGroups[0].Shifts", "Add at least one shift"));
    }

    // The form checks one field at a time as it changes, by selecting the field's property path. The group rules
    // are nested, so a path into one line must run that line's rule and no other.
    [Fact]
    public void Checking_one_field_runs_only_the_rules_of_that_line()
    {
        var entry = Workday(WorkGroup(days: []), WorkGroup(days: []));
        entry.Company = null;

        var result = Validate(entry, options => options.IncludeProperties("ShiftGroups[1].Days"));

        AssertOnly(result, ("ShiftGroups[1].Days", "Pick at least one day"));
    }

    [Theory]
    [InlineData("ar-IQ")]
    [InlineData("ku")]
    [InlineData("ru")]
    public void Every_message_is_translated(string culture)
    {
        foreach (var entry in EveryRuleBroken())
        {
            var english = Validate(entry).Errors;
            var translated = Validate(entry, culture: CultureInfo.GetCultureInfo(culture)).Errors;

            Assert.NotEmpty(english);
            Assert.Equal(english.Select(error => error.PropertyName), translated.Select(error => error.PropertyName));
            Assert.All(english.Zip(translated), pair => Assert.NotEqual(pair.First.ErrorMessage, pair.Second.ErrorMessage));
        }
    }

    // Two entries that together break every rule: one without shift groups, and one whose lines break the rest.
    private static IEnumerable<CompanyCalendarDTO> EveryRuleBroken()
    {
        yield return Workday();

        var entry = Workday(
            new CompanyCalendarShiftGroupDTO { Days = [] },
            WorkGroup(days: [9]),
            WorkGroup(days: [1, 1], shift: Shift(0, 0)),
            WorkGroup(shift: Shift(-1, TimeSpan.TicksPerDay)),
            WorkGroup(shift: Shift(8 * Hour, 8 * Hour)));
        entry.Company = null;
        entry.Branches = [];
        entry.EndDate = entry.StartDate!.Value.AddDays(-1);
        entry.Priority = -1;
        entry.WeekendGroups =
        [
            new CompanyCalendarWeekendGroupDTO(),
            WeekendGroup(new CompanyCalendarWeekendRuleItemDTO { Day = 7, Repeat = 0, Skip = -1 }),
        ];
        yield return entry;
    }

    private static void AssertOnly(ValidationResult result, params (string Path, string Message)[] expected) =>
        Assert.Equal(
            expected.OrderBy(error => error.Path).ThenBy(error => error.Message),
            result.Errors.Select(error => (error.PropertyName, error.ErrorMessage)).OrderBy(error => error.PropertyName).ThenBy(error => error.ErrorMessage));

    // The validator reads its messages when it is built, so the UI culture is set first: the invariant culture
    // (the English resources) unless a test asks for another.
    private static ValidationResult Validate(CompanyCalendarDTO entry,
        Action<ValidationStrategy<CompanyCalendarDTO>>? options = null, CultureInfo? culture = null)
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture ?? CultureInfo.InvariantCulture;
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddLocalization();
            var validator = new CompanyCalendarValidator(
                new ShiftIdentityLocalizer(services.BuildServiceProvider(), typeof(ShiftSoftwareLocalization.Identity.Resource)));

            return options is null ? validator.Validate(entry) : validator.Validate(entry, options);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static CompanyCalendarDTO Workday(params CompanyCalendarShiftGroupDTO[] groups) => Entry(CalendarEntryType.Workday, groups);

    private static CompanyCalendarDTO Holiday(params CompanyCalendarShiftGroupDTO[] groups) => Entry(CalendarEntryType.Holiday, groups);

    private static CompanyCalendarDTO Entry(CalendarEntryType type, CompanyCalendarShiftGroupDTO[] groups) => new()
    {
        Title = "Synthetic entry",
        StartDate = new DateTime(2026, 12, 25),
        EndDate = new DateTime(2026, 12, 26),
        EntryType = type,
        Priority = 1,
        Company = new ShiftEntitySelectDTO { Value = "1" },
        Branches = [new ShiftEntitySelectDTO { Value = "1" }],
        ShiftGroups = [.. groups],
    };

    // A holiday group as stored: some departments and a brand, every day, and one line with no times.
    private static CompanyCalendarShiftGroupDTO ScopeGroup() => new()
    {
        Departments = [new ShiftEntitySelectDTO { Value = "1" }, new ShiftEntitySelectDTO { Value = "6" }],
        Brands = [new ShiftEntitySelectDTO { Value = "1" }],
        Days = null,
        Shifts = [new CompanyCalendarShiftItemDTO { Title = "Synthetic entry" }],
    };

    private static CompanyCalendarShiftGroupDTO WorkGroup(List<int>? days = null, CompanyCalendarShiftItemDTO? shift = null) => new()
    {
        Departments = [new ShiftEntitySelectDTO { Value = "1" }],
        Brands = [new ShiftEntitySelectDTO { Value = "1" }],
        Days = days,
        Shifts = [shift ?? Shift(8 * Hour, 16 * Hour)],
    };

    private static CompanyCalendarShiftItemDTO Shift(long start, long end) =>
        new() { Title = "Synthetic shift", StartTimeTicks = start, EndTimeTicks = end };

    private static CompanyCalendarWeekendGroupDTO WeekendGroup(params CompanyCalendarWeekendRuleItemDTO[] rules) => new()
    {
        Departments = [new ShiftEntitySelectDTO { Value = "1" }],
        Brands = [new ShiftEntitySelectDTO { Value = "1" }],
        Weekends = rules.Length > 0 ? [.. rules] : [new CompanyCalendarWeekendRuleItemDTO { Day = 5, Repeat = 1, Skip = 0 }],
    };
}
