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
/// A calendar shift group's Days list through the real api/IdentityCompanyCalendar routes and an owned fixture
/// database. Days = null means "every day", so a read followed by an unchanged save must store exactly the text
/// that was there, and a client that sends Days = null on purpose must get it stored as null. The empty list
/// ("no day") is covered by the mapper tests in ShiftIdentity.Data.Tests.
/// </summary>
[Trait("Category", "Sql"), Trait("Category", "Http")]
public sealed class CompanyCalendarRoundTripSqlTests(SqlIdentityFixture fixture) : IClassFixture<SqlIdentityFixture>, IAsyncLifetime
{
    private const string OperatorName = "synthetic-calendar-admin";
    // Calendar read/write for the routes, plus data-level write on the company and branch dimensions.
    private const string OperatorTree = "{\"ShiftIdentityActions\":{\"CompanyCalendars\":[\"r\",\"w\"],\"DataLevelAccess\":{\"Companies\":[\"r\",\"w\"],\"Branches\":[\"r\",\"w\"]}}}";
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

    [Theory]
    [InlineData(null)]
    [InlineData(new[] { 1, 2 })]
    public async Task A_read_followed_by_an_unchanged_save_stores_the_same_shift_groups(int[]? days)
    {
        using var host = await HostAsync();
        var id = await SeedAsync(days);
        var before = await StoredShiftGroupsAsync(id);

        var entity = await GetEntityAsync(host, id);
        Assert.Equal(DaysJson(days), entity["ShiftGroups"]![0]!["Days"]?.ToJsonString() ?? "null");
        using var response = await host.Client.PutAsync($"/api/IdentityCompanyCalendar/{id}", Json(entity));
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var after = await StoredShiftGroupsAsync(id);
        Assert.Equal(before, after);
        Assert.Contains($"\"Days\":{DaysJson(days)}", after);
    }

    [Fact]
    public async Task A_new_entry_that_sends_days_as_null_stores_null()
    {
        using var host = await HostAsync();
        var copy = await GetEntityAsync(host, await SeedAsync(null));
        copy.Remove("ID");
        copy["Title"] = "Synthetic holiday copy";
        copy["ShiftGroups"]![0]!["Days"] = null;

        using var response = await host.Client.PostAsync("/api/IdentityCompanyCalendar", Json(copy));
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var created = JsonNode.Parse(await response.Content.ReadAsStringAsync())!["Entity"]!;

        Assert.Contains("\"Days\":null", await StoredShiftGroupsAsync(long.Parse(created["ID"]!.GetValue<string>())));
    }

    // A holiday scoped to one department and one brand, with the one zero-time shift such a holiday carries, the
    // way stored entries of that kind look. Department and brand ids are not checked against their tables.
    private async Task<long> SeedAsync(int[]? days)
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
                    Days = days?.ToList(),
                    Shifts = [new CompanyCalendarShift { Title = "Synthetic holiday", StartTimeTicks = 0, EndTimeTicks = 0 }],
                },
            ],
        };
        calendar.Branches.Add(new CompanyCalendarBranch { CompanyBranchID = branchID });
        db.CompanyCalendars.Add(calendar);
        await db.SaveChangesAsync();
        return calendar.ID;
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

    // The entity exactly as the API returned it: sending this back is an unchanged save.
    private static async Task<JsonObject> GetEntityAsync(LegacyIdentityHttpHost<IdentityTestDbContext> host, long id)
    {
        using var response = await host.Client.GetAsync($"/api/IdentityCompanyCalendar/{id}");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!["Entity"]!.AsObject();
    }

    private static StringContent Json(JsonNode body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    private static string DaysJson(int[]? days) => days is null ? "null" : "[" + string.Join(",", days) + "]";
}
