using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using ShiftIdentity.Tests.Infrastructure;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using Xunit;

namespace ShiftIdentity.Tests;

/// <summary>
/// The calendar entry rules on the api/IdentityCompanyCalendar save, through the real routes and an owned fixture
/// database. An entry that breaks a rule is refused with 400 and the "Model Validation Error" shape, one message
/// per property path, and nothing is written. A calendar with a rule that repeats every 0 weeks would make the
/// calendar events fail, so it must never be stored.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class CompanyCalendarValidationSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string OperatorName = "synthetic-calendar-validation-admin";
    // Calendar read/write for the routes, plus data-level write on the company and branch dimensions.
    private const string OperatorTree = "{\"ShiftIdentityActions\":{\"CompanyCalendars\":[\"r\",\"w\"],\"DataLevelAccess\":{\"Companies\":[\"r\",\"w\"],\"Branches\":[\"r\",\"w\"]}}}";
    private const long Hour = TimeSpan.TicksPerHour;
    private long companyID;
    private long branchID;

    public async ValueTask InitializeAsync()
    {
        await using var db = fixture.CreateContext();
        var template = await db.Users.AsNoTracking().SingleAsync(x => x.ID == fixture.UserID);
        companyID = template.CompanyID!.Value;
        branchID = template.CompanyBranchID!.Value;
        if (await db.Users.AnyAsync(x => x.Username == OperatorName)) return;
        // The legacy login verifies the HMAC hash format, so the operator is created with it directly.
        var hash = HashService.GenerateHash(fixture.Password);
        db.Users.Add(new User
        {
            Username = OperatorName, FullName = "Synthetic Operator", IsActive = true, PasswordHash = hash.PasswordHash, Salt = hash.Salt,
            CompanyID = template.CompanyID, CompanyBranchID = template.CompanyBranchID, CountryID = template.CountryID,
            RegionID = template.RegionID, AccessTree = OperatorTree
        });
        await db.SaveChangesAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task A_complete_workday_is_saved()
    {
        using var host = await HostAsync();

        using var response = await host.Client.PostAsync("/api/IdentityCompanyCalendar", Json(Workday(Group())));

        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_workday_without_shift_groups_is_refused_and_nothing_is_written()
    {
        using var host = await HostAsync();
        var before = await CountAsync();

        using var response = await host.Client.PostAsync("/api/IdentityCompanyCalendar", Json(Workday()));

        await AssertRefusedAsync(response, ("ShiftGroups", "Add at least one shift group"));
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task A_weekend_rule_that_repeats_every_zero_weeks_is_refused()
    {
        using var host = await HostAsync();
        var entry = Workday(Group());
        entry["WeekendGroups"] = new JsonArray(new JsonObject
        {
            ["Departments"] = Select("1"),
            ["Brands"] = Select("1"),
            ["Weekends"] = new JsonArray(new JsonObject { ["Day"] = 5, ["Repeat"] = 0, ["Skip"] = 0 }),
        });

        using var response = await host.Client.PostAsync("/api/IdentityCompanyCalendar", Json(entry));

        await AssertRefusedAsync(response, ("WeekendGroups[0].Weekends[0].Repeat", "The number of weeks must be at least 1"));
    }

    [Fact]
    public async Task A_save_that_sets_an_empty_day_list_is_refused_and_the_stored_groups_stay_as_they_were()
    {
        using var host = await HostAsync();
        var id = await SeedHolidayAsync();
        var before = await StoredShiftGroupsAsync(id);

        var entity = await GetEntityAsync(host, id);
        entity["ShiftGroups"]![0]!["Days"] = new JsonArray();
        using var response = await host.Client.PutAsync($"/api/IdentityCompanyCalendar/{id}", Json(entity));

        await AssertRefusedAsync(response, ("ShiftGroups[0].Days", "Pick at least one day"));
        Assert.Equal(before, await StoredShiftGroupsAsync(id));
        Assert.Contains("\"Days\":null", before);
    }

    private static async Task AssertRefusedAsync(HttpResponseMessage response, (string For, string Message) expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, body);
        var message = JsonNode.Parse(body)!["Message"]!;
        Assert.Equal("Model Validation Error", message["Title"]!.GetValue<string>());
        var field = Assert.Single(message["SubMessages"]!.AsArray());
        Assert.Equal(expected.For, field!["For"]!.GetValue<string>());
        Assert.Equal(expected.Message, Assert.Single(field["SubMessages"]!.AsArray())!["Title"]!.GetValue<string>());
    }

    private JsonObject Workday(params JsonObject[] groups) => new()
    {
        ["Title"] = "Synthetic workday",
        ["StartDate"] = "2026-10-01T00:00:00",
        ["EndDate"] = "2026-10-31T00:00:00",
        ["EntryType"] = (int)CalendarEntryType.Workday,
        ["Priority"] = 1,
        ["Company"] = new JsonObject { ["Value"] = companyID.ToString() },
        ["Branches"] = Select(branchID.ToString()),
        ["ShiftGroups"] = new JsonArray([.. groups]),
        ["WeekendGroups"] = new JsonArray(),
    };

    private static JsonObject Group() => new()
    {
        ["Departments"] = Select("1"),
        ["Brands"] = Select("1"),
        ["Days"] = null,
        ["Shifts"] = new JsonArray(new JsonObject { ["Title"] = "Synthetic shift", ["StartTimeTicks"] = 8 * Hour, ["EndTimeTicks"] = 16 * Hour }),
    };

    private static JsonArray Select(string value) => new(new JsonObject { ["Value"] = value });

    // A holiday limited to one department and one brand, with Days = null and the one line with no times such a
    // holiday carries: the shape stored holidays of that kind have, which the rules must accept.
    private async Task<long> SeedHolidayAsync()
    {
        await using var db = fixture.CreateContext();
        var calendar = new CompanyCalendar
        {
            Title = "Synthetic holiday",
            StartDate = new DateTime(2026, 12, 25),
            EndDate = new DateTime(2026, 12, 25),
            EntryType = CalendarEntryType.Holiday,
            Priority = 1,
            CompanyID = companyID,
            ShiftGroups =
            [
                new CompanyCalendarShiftGroup
                {
                    DepartmentIds = [1],
                    BrandIds = [1],
                    Days = null,
                    Shifts = [new CompanyCalendarShift { Title = "Synthetic holiday", StartTimeTicks = 0, EndTimeTicks = 0 }],
                },
            ],
        };
        calendar.Branches.Add(new CompanyCalendarBranch { CompanyBranchID = branchID });
        db.CompanyCalendars.Add(calendar);
        await db.SaveChangesAsync();
        return calendar.ID;
    }

    private async Task<int> CountAsync()
    {
        await using var db = fixture.CreateContext();
        return await db.CompanyCalendars.CountAsync();
    }

    // The column text itself, read past EF so the check sees exactly what is stored.
    private async Task<string> StoredShiftGroupsAsync(long id)
    {
        await using var db = fixture.CreateContext();
        return await db.Database
            .SqlQuery<string>($"SELECT [ShiftGroups] AS [Value] FROM [ShiftIdentity].[CompanyCalendars] WHERE [ID] = {id}")
            .SingleAsync();
    }

    private async Task<LegacyIdentityHttpHost<IdentityTestDbContext>> HostAsync()
    {
        var host = new LegacyIdentityHttpHost<IdentityTestDbContext>(fixture);
        using var login = await host.Client.PostAsJsonAsync("/api/Auth/Login", new LoginDTO { Username = OperatorName, Password = fixture.Password });
        Assert.True(login.StatusCode == HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var token = (await login.Content.ReadFromJsonAsync<ShiftEntityResponse<TokenDTO>>())!.Entity!.Token;
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return host;
    }

    private static async Task<JsonObject> GetEntityAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, long id)
    {
        using var response = await host.Client.GetAsync($"/api/IdentityCompanyCalendar/{id}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["Entity"]!.AsObject();
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");
}
