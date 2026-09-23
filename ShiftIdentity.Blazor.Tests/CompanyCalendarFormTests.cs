using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json.Nodes;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using ShiftSoftware.ShiftBlazor.Components;
using ShiftSoftware.ShiftBlazor.Enums;
using ShiftSoftware.ShiftBlazor.Extensions;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Extensions;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.CompanyCalendar;
using ShiftSoftware.TypeAuth.Blazor.Extensions;
using ShiftSoftware.TypeAuth.Core;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The production calendar entry form through the real ShiftEntityForm save path against a scripted HTTP transport.
/// Days = null means "every day", so an unchanged save must send it back as null. Rule messages show on the line they
/// belong to, and a holiday's groups (which limit it to some departments and brands) can be seen and saved.
/// </summary>
[Trait("Category", "Ui"), Collection("Company calendar form")]
public sealed class CompanyCalendarFormTests
{
    private const long Hour = TimeSpan.TicksPerHour;

    [Fact]
    public async Task An_unchanged_save_of_a_group_that_applies_every_day_sends_days_as_null()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        transport.Existing = Workday(WorkGroup(days: null));
        var cut = await OpenForEditAsync(context);

        // Null shows as all seven days picked, which the select names "Every day".
        Assert.Contains("Every day", cut.Find("[data-testid=calendar-group-days]").OuterHtml);
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
        var group = Body(transport)["shiftGroups"]![0]!;
        Assert.Null(group["days"]);
    }

    [Fact]
    public async Task A_workday_without_shift_groups_shows_the_message_and_is_not_saved()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        var cut = context.Render<CompanyCalendarForm>();
        var form = cut.FindComponent<ShiftEntityForm<CompanyCalendarDTO>>();
        FillGeneral(form.Instance.Value);
        await ChangeEntryTypeAsync(cut, CalendarEntryType.Workday);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Add at least one shift group", cut.Find(".calendar-shift-groups").TextContent));
        Assert.Empty(transport.Saves);
    }

    [Fact]
    public async Task A_group_without_departments_shows_the_message_on_its_department_field()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        var group = WorkGroup(days: null);
        group.Departments = [];
        transport.Existing = Workday(group);
        var cut = await OpenForEditAsync(context);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Pick at least one department", cut.Find("[data-testid=calendar-group-departments]").TextContent));
        Assert.DoesNotContain("Pick at least one department", cut.Find("[data-testid=calendar-group-brands]").TextContent);
        Assert.Empty(transport.Saves);
    }

    [Fact]
    public async Task A_group_with_no_day_picked_shows_the_message_on_its_days_field()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        transport.Existing = Workday(WorkGroup(days: []));
        var cut = await OpenForEditAsync(context);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Pick at least one day", cut.Find("[data-testid=calendar-group-days]").TextContent));
        Assert.Empty(transport.Saves);
    }

    [Fact]
    public async Task A_workday_line_with_no_times_shows_the_message_on_its_start_field()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        transport.Existing = Workday(WorkGroup(days: null, start: 0, end: 0));
        var cut = await OpenForEditAsync(context);

        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Contains("Set the start and end times", cut.Find(".calendar-shift-start").TextContent));
        Assert.Empty(transport.Saves);
    }

    [Fact]
    public async Task A_shift_that_ends_before_it_starts_is_marked_as_ending_the_next_day()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        transport.Existing = Workday(WorkGroup(days: null, start: 22 * Hour, end: 6 * Hour));

        var cut = await OpenForEditAsync(context);

        Assert.Contains("Ends the next day", cut.Find(".calendar-shift-end").TextContent);
    }

    [Fact]
    public async Task A_holiday_limited_to_some_departments_shows_its_groups_and_an_unchanged_save_keeps_them()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        transport.Existing = Holiday(ScopeGroup());
        var cut = await OpenForEditAsync(context);

        var scope = cut.Find(".calendar-holiday-scope");
        Assert.Contains("Applies to", scope.TextContent);
        Assert.Single(cut.FindAll(".calendar-holiday-scope [data-testid=calendar-group-departments]"));
        Assert.Empty(cut.FindAll(".calendar-shift-start"));
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
        var group = Body(transport)["shiftGroups"]![0]!;
        Assert.Null(group["days"]);
        var line = Assert.Single(group["shifts"]!.AsArray())!;
        Assert.Equal((0L, 0L), (line["startTimeTicks"]!.GetValue<long>(), line["endTimeTicks"]!.GetValue<long>()));
    }

    [Fact]
    public async Task A_group_added_to_a_new_holiday_is_saved_with_the_line_a_holiday_group_needs()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        var cut = context.Render<CompanyCalendarForm>();
        var form = cut.FindComponent<ShiftEntityForm<CompanyCalendarDTO>>();
        FillGeneral(form.Instance.Value);
        await ChangeEntryTypeAsync(cut, CalendarEntryType.Holiday);

        cut.Find(".calendar-holiday-scope button.mud-button-filled-info").Click();
        // The department and brand pickers are remote autocompletes, so the picked values are set on the bound model.
        var group = Assert.Single(form.Instance.Value.ShiftGroups);
        group.Departments = [new ShiftEntitySelectDTO { Value = "2", Text = "Synthetic department" }];
        group.Brands = [new ShiftEntitySelectDTO { Value = "3", Text = "Synthetic brand" }];
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Single(transport.Saves));
        var saved = Body(transport)["shiftGroups"]![0]!;
        Assert.Null(saved["days"]);
        var line = Assert.Single(saved["shifts"]!.AsArray())!;
        Assert.Equal(("Synthetic holiday", 0L, 0L),
            (line["title"]!.GetValue<string>(), line["startTimeTicks"]!.GetValue<long>(), line["endTimeTicks"]!.GetValue<long>()));
    }

    [Fact]
    public async Task Changing_the_type_clears_the_groups_of_the_previous_type_and_changing_back_brings_them_back()
    {
        await using var context = CompanyCalendarFormHarness.Create(out var transport);
        var group = WorkGroup(days: [1, 2]);
        var weekend = WeekendGroup();
        transport.Existing = Workday(group);
        transport.Existing.WeekendGroups = [weekend];
        var cut = await OpenForEditAsync(context);
        var form = cut.FindComponent<ShiftEntityForm<CompanyCalendarDTO>>();
        var loadedGroup = Assert.Single(form.Instance.Value.ShiftGroups);
        var loadedWeekend = Assert.Single(form.Instance.Value.WeekendGroups);

        await ChangeEntryTypeAsync(cut, CalendarEntryType.Holiday);

        Assert.Empty(form.Instance.Value.ShiftGroups);
        Assert.Empty(form.Instance.Value.WeekendGroups);
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".calendar-holiday-scope")));
        Assert.Empty(cut.FindAll(".calendar-weekend-groups"));

        await ChangeEntryTypeAsync(cut, CalendarEntryType.Workday);

        Assert.Same(loadedGroup, Assert.Single(form.Instance.Value.ShiftGroups));
        Assert.Same(loadedWeekend, Assert.Single(form.Instance.Value.WeekendGroups));
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll(".calendar-shift-groups")));
    }

    [Fact]
    public void The_calendar_page_keeps_its_earlier_address()
    {
        var routes = typeof(CompanyCalendarPage).GetCustomAttributes<RouteAttribute>().Select(route => route.Template);

        Assert.Equal(["Identity/CompanyCalendarMudPage", "Identity/CompanyCalendarPage"], routes.Order());
    }

    private static async Task<IRenderedComponent<CompanyCalendarForm>> OpenForEditAsync(BunitContext context)
    {
        var cut = context.Render<CompanyCalendarForm>(p => p.Add(x => x.Key, "42"));
        var form = cut.FindComponent<ShiftEntityForm<CompanyCalendarDTO>>();
        cut.WaitForState(() => form.Instance.Value.ID == "42" && form.Instance.TaskInProgress == FormTasks.None);
        await cut.InvokeAsync(form.Instance.EditItem);
        cut.WaitForAssertion(() => Assert.Equal(FormModes.Edit, form.Instance.Mode));
        return cut;
    }

    private static async Task ChangeEntryTypeAsync(IRenderedComponent<CompanyCalendarForm> cut, CalendarEntryType type)
    {
        var select = cut.FindComponent<MudSelect<CalendarEntryType?>>();
        await cut.InvokeAsync(() => select.Instance.ValueChanged.InvokeAsync(type));
    }

    private static void FillGeneral(CompanyCalendarDTO item)
    {
        item.Title = "Synthetic holiday";
        item.StartDate = new DateTime(2026, 12, 25);
        item.EndDate = new DateTime(2026, 12, 25);
        item.Company = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Company" };
        item.Branches = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" }];
    }

    private static JsonNode Body(CompanyCalendarFormTransport transport) => JsonNode.Parse(transport.Saves.Single().Body)!;

    private static CompanyCalendarDTO Workday(params CompanyCalendarShiftGroupDTO[] groups) => Entry(CalendarEntryType.Workday, "Synthetic workday", groups);

    private static CompanyCalendarDTO Holiday(params CompanyCalendarShiftGroupDTO[] groups) => Entry(CalendarEntryType.Holiday, "Synthetic holiday", groups);

    private static CompanyCalendarDTO Entry(CalendarEntryType type, string title, CompanyCalendarShiftGroupDTO[] groups) => new()
    {
        ID = "42",
        Title = title,
        StartDate = new DateTime(2026, 12, 25),
        EndDate = new DateTime(2026, 12, 25),
        EntryType = type,
        Priority = 1,
        Company = new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Company" },
        Branches = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic Branch" }],
        ShiftGroups = [.. groups],
    };

    // A holiday group as stored: some departments and a brand, every day, and one line with no times.
    private static CompanyCalendarShiftGroupDTO ScopeGroup() => new()
    {
        Departments = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic department" }],
        Brands = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic brand" }],
        Days = null,
        Shifts = [new CompanyCalendarShiftItemDTO { Title = "Synthetic holiday" }],
    };

    private static CompanyCalendarShiftGroupDTO WorkGroup(List<int>? days, long start = 8 * Hour, long end = 16 * Hour) => new()
    {
        Departments = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic department" }],
        Brands = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic brand" }],
        Days = days,
        Shifts = [new CompanyCalendarShiftItemDTO { Title = "Synthetic shift", StartTimeTicks = start, EndTimeTicks = end }],
    };

    private static CompanyCalendarWeekendGroupDTO WeekendGroup() => new()
    {
        Departments = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic department" }],
        Brands = [new ShiftEntitySelectDTO { Value = "1", Text = "Synthetic brand" }],
        Weekends = [new CompanyCalendarWeekendRuleItemDTO { Day = 5, Repeat = 1, Skip = 0 }],
    };
}

/// <summary>An operator with calendar read/write and the transport below.</summary>
internal static class CompanyCalendarFormHarness
{
    public static BunitContext Create(out CompanyCalendarFormTransport transport)
    {
        var context = new BunitContext();
        transport = new();
        context.Services.AddSingleton(new HttpClient(transport) { BaseAddress = new Uri("https://identity.invalid/") });
        context.Services.AddShiftBlazor(o => o.ShiftConfiguration = c => c.BaseAddress = "https://identity.invalid/");
        context.Services.AddShiftIdentityDashboardBlazor(_ => { });
        context.Services.AddTransient(sp => new ShiftIdentityLocalizer(sp, typeof(ShiftSoftwareLocalization.Identity.Resource)));
        var auth = context.AddAuthorization(); auth.SetAuthorized("synthetic");
        auth.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, "{\"ShiftIdentityActions\":{\"CompanyCalendars\":[\"r\",\"w\"]}}"));
        context.Services.AddTypeAuth(o => o.AddActionTree<ShiftIdentityActions>());
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        return context;
    }
}

/// <summary>Records saves, serves one existing entry by key, and answers every other request with an empty OData page.</summary>
internal sealed class CompanyCalendarFormTransport : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string Body)> Saves { get; } = [];

    /// <summary>Served for GET IdentityCompanyCalendar/{ID}; the form opens it in view mode.</summary>
    public CompanyCalendarDTO? Existing { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Saves.Add((request.Method, path, body));
            var saved = System.Text.Json.JsonSerializer.Deserialize<CompanyCalendarDTO>(body, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
            saved.ID ??= "42";
            return new(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new ShiftEntityResponse<CompanyCalendarDTO>(saved))
            };
        }
        if (request.Method == HttpMethod.Get && Existing is not null && path == $"/IdentityCompanyCalendar/{Existing.ID}")
            return new(HttpStatusCode.OK) { Content = JsonContent.Create(new ShiftEntityResponse<CompanyCalendarDTO>(Existing)) };
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"value\":[]}", System.Text.Encoding.UTF8, "application/json") };
    }
}
