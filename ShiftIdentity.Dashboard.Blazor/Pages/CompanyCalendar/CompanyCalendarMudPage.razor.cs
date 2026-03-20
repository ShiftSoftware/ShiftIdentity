using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using System.Globalization;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.CompanyCalendar;

public partial class CompanyCalendarMudPage
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    private const string StorageKey = "CompanyCalendar_Preferences";

    private bool _loading;
    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private ShiftEntitySelectDTO? _selectedCompany;
    private ShiftEntitySelectDTO? _selectedBranch;
    private ShiftEntitySelectDTO? _selectedBrand;
    private ShiftEntitySelectDTO? _selectedDepartment;

    private List<CalendarEventDTO> _events = new();
    private List<CalendarDay[]> _weeks = new();
    private string[] _dayHeaders = Array.Empty<string>();

    protected override async Task OnInitializedAsync()
    {
        BuildDayHeaders();
        BuildWeeks();

        await RestorePreferences();

        await base.OnInitializedAsync();
    }

    #region Month Navigation

    private async Task PreviousMonth()
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        BuildWeeks();
        await LoadEvents();
    }

    private async Task NextMonth()
    {
        _currentMonth = _currentMonth.AddMonths(1);
        BuildWeeks();
        await LoadEvents();
    }

    private async Task GoToToday()
    {
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BuildWeeks();
        await LoadEvents();
    }

    #endregion

    #region Filter Handlers

    private async Task OnCompanyChanged(ShiftEntitySelectDTO? company)
    {
        _selectedCompany = company;
        _selectedBranch = null;
        _events.Clear();
        BuildWeeks();

        await SavePreferences();

        if (company?.Value is not null)
        {
            await LoadEvents();
        }
    }

    private async Task OnBranchChanged(ShiftEntitySelectDTO? branch)
    {
        _selectedBranch = branch;
        await SavePreferences();
        await LoadEvents();
    }

    private async Task OnBrandhChanged(ShiftEntitySelectDTO? brand)
    {
        _selectedBrand = brand;
        await SavePreferences();
        await LoadEvents();
    }

    private async Task OnDepartmentChanged(ShiftEntitySelectDTO? department)
    {
        _selectedDepartment = department;
        await SavePreferences();
        await LoadEvents();
    }

    #endregion

    #region Data Loading

    private async Task LoadEvents()
    {
        if (_selectedCompany?.Value is null || _selectedBranch?.Value is null || _selectedDepartment?.Value is null)
            return;

        _loading = true;
        StateHasChanged();

        try
        {
            var viewStart = _weeks.First()[0].Date;
            var viewEnd = _weeks.Last()[6].Date.AddDays(1);

            var filter = new CalendarFilterDTO
            {
                ViewStartDate = viewStart,
                ViewEndDate = viewEnd,
                CompanyHashId = _selectedCompany.Value,
                ViewBranchHashIds = _selectedBranch?.Value is not null
                    ? new List<string> { _selectedBranch.Value }
                    : null,
                ViewDepartmentHashIds = _selectedDepartment?.Value is not null
                    ? new List<string> { _selectedDepartment.Value }
                    : null,
            };

            var response = await Http.PostAsJsonAsync(
                $"{Constants.IdentityRoutePreifix}CompanyCalendar/GetCalendarEvents", filter);

            if (response.IsSuccessStatusCode)
            {
                _events = await response.Content.ReadFromJsonAsync<List<CalendarEventDTO>>() ?? new();
            }

            BuildWeeks();
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    #endregion

    #region Grid Building

    private void BuildDayHeaders()
    {
        var culture = CultureInfo.CurrentCulture;
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        _dayHeaders = Enumerable.Range(0, 7)
            .Select(i => culture.DateTimeFormat.AbbreviatedDayNames[((int)firstDay + i) % 7])
            .ToArray();
    }

    private void BuildWeeks()
    {
        var culture = CultureInfo.CurrentCulture;
        var firstDayOfWeek = culture.DateTimeFormat.FirstDayOfWeek;

        var firstOfMonth = _currentMonth;
        var daysInMonth = DateTime.DaysInMonth(firstOfMonth.Year, firstOfMonth.Month);
        var lastOfMonth = new DateTime(firstOfMonth.Year, firstOfMonth.Month, daysInMonth);

        var offset = ((int)firstOfMonth.DayOfWeek - (int)firstDayOfWeek + 7) % 7;
        var gridStart = firstOfMonth.AddDays(-offset);

        var endOffset = (6 - (int)lastOfMonth.DayOfWeek + (int)firstDayOfWeek + 7) % 7;
        var gridEnd = lastOfMonth.AddDays(endOffset);

        var totalDays = (gridEnd - gridStart).Days + 1;
        var totalWeeks = totalDays / 7;

        var eventsByDate = _events
            .Where(e => DateTime.TryParse(e.Start, out _))
            .GroupBy(e => DateTime.Parse(e.Start).Date)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Priority).ToList());

        _weeks = new List<CalendarDay[]>(totalWeeks);

        for (var w = 0; w < totalWeeks; w++)
        {
            var week = new CalendarDay[7];
            for (var d = 0; d < 7; d++)
            {
                var date = gridStart.AddDays(w * 7 + d);
                week[d] = new CalendarDay
                {
                    Date = date,
                    IsCurrentMonth = date.Month == _currentMonth.Month && date.Year == _currentMonth.Year,
                    IsToday = date == DateTime.Today,
                    Events = eventsByDate.TryGetValue(date, out var evts) ? evts : new(),
                };
            }
            _weeks.Add(week);
        }
    }

    #endregion

    #region Navigation

    private void OnDayClicked(DateTime date)
    {
        Nav.NavigateTo($"{Constants.IdentityRoutePreifix}/{nameof(CompanyCalendarForm)}");
    }

    private void OnEventClicked(long calendarId)
    {
        Nav.NavigateTo($"{Constants.IdentityRoutePreifix}/{nameof(CompanyCalendarForm)}/{calendarId}");
    }

    private void NavigateToNewEntry()
    {
        Nav.NavigateTo($"{Constants.IdentityRoutePreifix}/{nameof(CompanyCalendarForm)}");
    }

    #endregion

    #region Helpers

    private static string GetEventCssClass(CalendarEventDTO evt) => (CalendarEntryStatus)evt.Status switch
    {
        CalendarEntryStatus.WorkPeriod => "chip-work",
        CalendarEntryStatus.Weekend => "chip-weekend",
        CalendarEntryStatus.Holiday => "chip-holiday",
        CalendarEntryStatus.InactiveWorkPeriod => "chip-inactive",
        _ => "",
    };

    private class CalendarDay
    {
        public DateTime Date { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsToday { get; set; }
        public List<CalendarEventDTO> Events { get; set; } = new();
    }

    private class ODataResponse<T>
    {
        public List<T>? Value { get; set; }
    }

    #endregion

    #region Preferences Persistence

    private async Task SavePreferences()
    {
        var prefs = new CalendarPreferences
        {
            Company = _selectedCompany,
            Branch = _selectedBranch,
            Department = _selectedDepartment,
            Brand = _selectedBrand,
        };

        await LocalStorage.SetItemAsync(StorageKey, prefs);
    }

    private async Task RestorePreferences()
    {
        try
        {
            var prefs = await LocalStorage.GetItemAsync<CalendarPreferences>(StorageKey);
            if (prefs is null)
                return;

            _selectedCompany = prefs.Company;
            _selectedBranch = prefs.Branch;
            _selectedDepartment = prefs.Department;
            _selectedBrand = prefs.Brand;

            await LoadEvents();
        }
        catch
        {
            // If stored data is corrupt, just start fresh
            await LocalStorage.RemoveItemAsync(StorageKey);
        }
    }

    private class CalendarPreferences
    {
        public ShiftEntitySelectDTO? Company { get; set; }
        public ShiftEntitySelectDTO? Branch { get; set; }
        public ShiftEntitySelectDTO? Department { get; set; }
        public ShiftEntitySelectDTO? Brand { get; set; }
    }

    #endregion
}
