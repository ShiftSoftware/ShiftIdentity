using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShiftMapper;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.Replication;
using Xunit;

namespace ShiftIdentity.Data.Tests;

/// <summary>
/// The identity mapper must keep a calendar shift group's Days list exactly as it is. Days = null means "every
/// day" and an empty list means "no day", so turning one into the other changes the days the group applies to.
/// The maps below are the ones the repository runs: entity to view for a read, and view onto the loaded entity
/// for a save.
/// </summary>
public class CompanyCalendarMapperTests
{
    // The options the database column is written with (ShiftIdentityDbContext), so a serialized list here is
    // the text that would be stored.
    private static readonly JsonSerializerOptions ColumnJson = new() { PropertyNamingPolicy = null };

    [Theory]
    [InlineData(null)]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 2 })]
    public void A_read_returns_the_stored_days(int[]? days)
    {
        var view = Mapper().Map<CompanyCalendar, CompanyCalendarDTO>(Stored(days));

        AssertDays(days, Assert.Single(view.ShiftGroups).Days);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 2 })]
    public void A_new_entry_stores_the_days_that_were_sent(int[]? days)
    {
        var sent = Mapper().Map<CompanyCalendar, CompanyCalendarDTO>(Stored(days));

        var created = Mapper().Map<CompanyCalendarDTO, CompanyCalendar>(sent, new CompanyCalendar());

        AssertDays(days, Assert.Single(created.ShiftGroups).Days);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new int[0])]
    [InlineData(new[] { 1, 2 })]
    public void An_unchanged_save_after_a_read_stores_the_same_text(int[]? days)
    {
        var mapper = Mapper();
        var stored = Stored(days);
        var before = JsonSerializer.Serialize(stored.ShiftGroups, ColumnJson);

        var view = mapper.Map<CompanyCalendar, CompanyCalendarDTO>(stored);
        var saved = mapper.Map<CompanyCalendarDTO, CompanyCalendar>(view, Stored(days));

        Assert.Equal(before, JsonSerializer.Serialize(saved.ShiftGroups, ColumnJson));
        if (days is null)
            Assert.Contains("\"Days\":null", before);
    }

    [Fact]
    public void A_group_that_sets_days_to_null_on_a_save_is_stored_as_every_day()
    {
        var mapper = Mapper();
        var view = mapper.Map<CompanyCalendar, CompanyCalendarDTO>(Stored([1, 2]));

        Assert.Single(view.ShiftGroups).Days = null;
        var saved = mapper.Map<CompanyCalendarDTO, CompanyCalendar>(view, Stored([1, 2]));

        Assert.Null(Assert.Single(saved.ShiftGroups).Days);
    }

    [Fact]
    public void Departments_brands_and_shifts_still_round_trip()
    {
        var mapper = Mapper();
        var stored = Stored(null);

        var view = mapper.Map<CompanyCalendar, CompanyCalendarDTO>(stored);
        var saved = mapper.Map<CompanyCalendarDTO, CompanyCalendar>(view, Stored(null));

        var group = Assert.Single(saved.ShiftGroups);
        Assert.Equal([1L, 6L], group.DepartmentIds);
        Assert.Equal([1L], group.BrandIds);
        var shift = Assert.Single(group.Shifts);
        Assert.Equal(("Synthetic holiday", 0L, 0L), (shift.Title, shift.StartTimeTicks, shift.EndTimeTicks));
    }

    private static void AssertDays(int[]? expected, List<int>? actual)
    {
        if (expected is null)
            Assert.Null(actual);
        else
            Assert.Equal(expected, Assert.IsType<List<int>>(actual));
    }

    // A holiday scoped to two departments and one brand, with the one zero-time shift such a holiday carries.
    private static CompanyCalendar Stored(int[]? days) => new()
    {
        ID = 7,
        Title = "Synthetic holiday",
        StartDate = new DateTime(2026, 12, 25),
        EndDate = new DateTime(2026, 12, 25),
        EntryType = CalendarEntryType.Holiday,
        Priority = 1,
        CompanyID = 3,
        ShiftGroups =
        [
            new CompanyCalendarShiftGroup
            {
                DepartmentIds = [1, 6],
                BrandIds = [1],
                Days = days?.ToList(),
                Shifts = [new CompanyCalendarShift { Title = "Synthetic holiday", StartTimeTicks = 0, EndTimeTicks = 0 }],
            },
        ],
    };

    // The generated identity mapper, resolved the way a host resolves it. The calendar group maps read the
    // hash-id service from the container, so a container is needed.
    private static IMapper Mapper()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHashIdService>(new HashIdService(Options.Create(new ShiftEntityOptions())));
        services.AddShiftIdentityReplicationMapper();
        return services.BuildServiceProvider().GetRequiredService<IMapper>();
    }
}
