using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components;
using MudBlazor;
using ShiftSoftware.ShiftBlazor.Services;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using ShiftSoftware.ShiftIdentity.Core.Enums;
using System.Globalization;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.CompanyCalendar;

public partial class CompanyCalendarMudPage : IDisposable
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private ShiftModal DialogService { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    private const string StorageKey = "CompanyCalendar_Preferences";
    private const int MonthNavDebounceMs = 300;
    private const int SkeletonDelayMs = 250;

    private bool _loading;
    private bool _showSkeleton;
    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private CancellationTokenSource? _loadCts;
    private Timer? _debounceTimer;
    private Timer? _skeletonTimer;

    private ShiftEntitySelectDTO? _selectedCompany;
    private ShiftEntitySelectDTO? _selectedBranch;
    private ShiftEntitySelectDTO? _selectedBrand;
    private ShiftEntitySelectDTO? _selectedDepartment;

    private List<CalendarEventDTO> _events = new();
    private List<CalendarDay[]> _weeks = new();
    private string[] _dayHeaders = Array.Empty<string>();

    // Drag-to-select state
    private DateTime? _dragStart;
    private DateTime? _dragEnd;
    private bool _isDragging;

    protected override async Task OnInitializedAsync()
    {
        BuildDayHeaders();
        BuildWeeks();

        await RestorePreferences();

        await base.OnInitializedAsync();
    }

    #region Month Navigation

    private void PreviousMonth()
    {
        _currentMonth = _currentMonth.AddMonths(-1);
        _events.Clear();
        BuildWeeks();
        ScheduleDebouncedLoad();
    }

    private void NextMonth()
    {
        _currentMonth = _currentMonth.AddMonths(1);
        _events.Clear();
        BuildWeeks();
        ScheduleDebouncedLoad();
    }

    private void GoToToday()
    {
        _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        _events.Clear();
        BuildWeeks();
        ScheduleDebouncedLoad();
    }

    private void ScheduleDebouncedLoad()
    {
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            InvokeAsync(async () =>
            {
                await LoadEvents();
            });
        }, null, MonthNavDebounceMs, Timeout.Infinite);
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

        // Cancel any in-flight request
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        var cts = _loadCts = new CancellationTokenSource();

        _loading = true;
        _skeletonTimer?.Dispose();
        _skeletonTimer = new Timer(_ =>
        {
            InvokeAsync(() =>
            {
                if (_loading)
                {
                    _showSkeleton = true;
                    StateHasChanged();
                }
            });
        }, null, SkeletonDelayMs, Timeout.Infinite);

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
                $"{Constants.IdentityRoutePreifix}CompanyCalendar/GetCalendarEvents", filter, cts.Token);

            cts.Token.ThrowIfCancellationRequested();

            if (response.IsSuccessStatusCode)
            {
                _events = await response.Content.ReadFromJsonAsync<List<CalendarEventDTO>>(cts.Token) ?? new();
            }

            BuildWeeks();
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            // A newer request superseded this one — do nothing
            return;
        }
        finally
        {
            // Only update loading state if this is still the active request
            if (cts == _loadCts)
            {
                _loading = false;
                _showSkeleton = false;
                _skeletonTimer?.Dispose();
                StateHasChanged();
            }
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

    #region Navigation & Selection

    private void OnCellPointerDown(DateTime date)
    {
        _dragStart = date;
        _dragEnd = date;
        _isDragging = true;
    }

    private void OnCellPointerEnter(DateTime date)
    {
        if (_isDragging)
        {
            _dragEnd = date;
        }
    }

    private async Task OnCellPointerUp(DateTime date)
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        var start = _dragStart!.Value;
        var end = date;

        // Normalize so start <= end
        if (end < start)
            (start, end) = (end, start);

        _dragStart = null;
        _dragEnd = null;

        await OpenFormDialog(startDate: start, endDate: end);
    }

    private void CancelDrag()
    {
        _isDragging = false;
        _dragStart = null;
        _dragEnd = null;
    }

    private bool IsInDragRange(DateTime date)
    {
        if (!_isDragging || _dragStart is null || _dragEnd is null)
            return false;

        var rangeStart = _dragStart.Value <= _dragEnd.Value ? _dragStart.Value : _dragEnd.Value;
        var rangeEnd = _dragStart.Value <= _dragEnd.Value ? _dragEnd.Value : _dragStart.Value;

        return date >= rangeStart && date <= rangeEnd;
    }

    private async Task OpenFormDialog(long? calendarId = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var parameters = new Dictionary<string, object>();

        if (calendarId is not null)
        {
            parameters["key"] = calendarId.Value.ToString();
        }
        else
        {
            if (startDate is not null)
                parameters[nameof(CompanyCalendarForm.InitialStartDate)] = startDate.Value;

            if (endDate is not null)
                parameters[nameof(CompanyCalendarForm.InitialEndDate)] = endDate.Value;

            if (_selectedCompany is not null)
                parameters[nameof(CompanyCalendarForm.InitialCompany)] = _selectedCompany;

            if (_selectedBranch is not null)
                parameters[nameof(CompanyCalendarForm.InitialBranches)] = new List<ShiftEntitySelectDTO> { _selectedBranch };
        }

        var dialogReference = await DialogService.Open<CompanyCalendarForm>(parameters: parameters);

        if (!(dialogReference?.Canceled ?? false) && dialogReference?.Data is not null)
        {
            _events.Clear();
            BuildWeeks();
            ScheduleDebouncedLoad();
        }
    }

    private async Task OnEventClicked(long calendarId)
    {
        await OpenFormDialog(calendarId: calendarId);
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

    public void Dispose()
    {
        _debounceTimer?.Dispose();
        _skeletonTimer?.Dispose();
        _loadCts?.Cancel();
        _loadCts?.Dispose();
    }
}
