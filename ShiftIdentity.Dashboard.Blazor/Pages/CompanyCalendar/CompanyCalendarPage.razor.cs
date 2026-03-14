using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ShiftSoftware.ShiftEntity.Model.Dtos;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.CompanyCalendar;
using System.Net.Http.Json;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.CompanyCalendar;

public partial class CompanyCalendarPage : IAsyncDisposable
{
    [Inject] private HttpClient Http { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;
    [Inject] private NavigationManager Nav { get; set; } = default!;

    private DotNetObjectReference<CompanyCalendarPage>? _dotNetRef;
    private bool _calendarInitialized;
    private bool _loading;
    private string? _currentViewStart;
    private string? _currentViewEnd;

    private ShiftEntitySelectDTO? _selectedCompany;
    private ShiftEntitySelectDTO? _selectedBranch;
    private ShiftEntitySelectDTO? _selectedDepartment;

    #region JS Callbacks

    [JSInvokable]
    public async Task OnDatesChanged(string viewStart, string viewEnd)
    {
        _currentViewStart = viewStart;
        _currentViewEnd = viewEnd;
        await LoadEvents();
    }

    [JSInvokable]
    public Task OnDateRangeSelected(string start, string end)
    {
        Nav.NavigateTo($"{Constants.IdentityRoutePreifix}/{nameof(CompanyCalendarForm)}");
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnEventClicked(int calendarId)
    {
        Nav.NavigateTo($"{Constants.IdentityRoutePreifix}/{nameof(CompanyCalendarForm)}/{calendarId}");
        return Task.CompletedTask;
    }

    #endregion

    #region Filter Handlers

    private async Task OnCompanyChanged(ShiftEntitySelectDTO? company)
    {
        _selectedCompany = company;
        _selectedBranch = null;

        if (company?.Value is not null)
        {
            if (!_calendarInitialized)
            {
                await InitCalendarIfNeeded();
            }
            else
            {
                await LoadEvents();
            }
        }
        else
        {
            if (_calendarInitialized)
            {
                await JS.InvokeVoidAsync("fullCalendarInterop.destroy", "fc-company-calendar");
                _calendarInitialized = false;
            }
        }
    }

    private async Task OnBranchChanged(ShiftEntitySelectDTO? branch)
    {
        _selectedBranch = branch;
        await LoadEvents();
    }

    private async Task OnDepartmentChanged(ShiftEntitySelectDTO? department)
    {
        _selectedDepartment = department;
        await LoadEvents();
    }

    #endregion

    #region Calendar Init

    private async Task EnsureScriptLoaded()
    {
        var isLoaded = await JS.InvokeAsync<bool>("eval", "typeof window.fullCalendarInterop !== 'undefined'");
        if (!isLoaded)
        {
            // Dynamically inject the script tag and return void
            await JS.InvokeAsync<bool>("eval",
                "(function(){" +
                "var s=document.createElement('script');" +
                "s.src='_content/ShiftSoftware.ShiftIdentity.Dashboard.Blazor/js/fullcalendar-interop.js';" +
                "document.head.appendChild(s);" +
                "return true;" +
                "})()");

            // Wait for the script to load and execute
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(100);
                isLoaded = await JS.InvokeAsync<bool>("eval", "typeof window.fullCalendarInterop !== 'undefined'");
                if (isLoaded) break;
            }
        }
    }

    private async Task InitCalendarIfNeeded()
    {
        if (_calendarInitialized) return;

        await Task.Yield();
        StateHasChanged();
        await Task.Delay(50);

        await EnsureScriptLoaded();

        _dotNetRef = DotNetObjectReference.Create(this);
        await JS.InvokeVoidAsync("fullCalendarInterop.init", "fc-company-calendar", _dotNetRef);
        _calendarInitialized = true;
    }

    #endregion

    #region Data Loading

    private async Task LoadEvents()
    {
        if (!_calendarInitialized || _currentViewStart is null || _selectedCompany?.Value is null)
            return;

        _loading = true;
        StateHasChanged();

        try
        {
            var filter = new CalendarFilterDTO
            {
                ViewStartDate = DateTime.Parse(_currentViewStart),
                ViewEndDate = DateTime.Parse(_currentViewEnd),
                CompanyHashId = _selectedCompany.Value,
                ViewBranchHashIds = _selectedBranch?.Value is not null
                    ? [_selectedBranch.Value]
                    : null,
                ViewDepartmentHashIds = _selectedDepartment?.Value is not null
                    ? [_selectedDepartment.Value]
                    : null,
            };

            var response = await Http.PostAsJsonAsync($"{Constants.IdentityRoutePreifix}CompanyCalendar/GetCalendarEvents", filter);

            if (response.IsSuccessStatusCode)
            {
                var events = await response.Content.ReadFromJsonAsync<List<CalendarEventDTO>>();

                if (events is not null)
                {
                    await JS.InvokeVoidAsync("fullCalendarInterop.setEvents", "fc-company-calendar", events);
                }
            }
        }
        finally
        {
            _loading = false;
            StateHasChanged();
        }
    }

    #endregion

    public async ValueTask DisposeAsync()
    {
        if (_calendarInitialized)
        {
            try
            {
                await JS.InvokeVoidAsync("fullCalendarInterop.destroy", "fc-company-calendar");
            }
            catch (JSDisconnectedException) { }
        }

        _dotNetRef?.Dispose();
    }
}
