using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Providers;

/// <summary>
/// Server-side AuthenticationStateProvider that reads from HttpContext.User
/// and persists the claims to PersistentComponentState for the WASM handoff.
/// Used with cookie-based authentication in Blazor Web App.
/// </summary>
public class PersistingCookieAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly PersistentComponentState _persistentState;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private PersistingComponentStateSubscription _subscription;
    private Task<AuthenticationState>? _authStateTask;

    public PersistingCookieAuthStateProvider(
        PersistentComponentState persistentState,
        IHttpContextAccessor httpContextAccessor)
    {
        _persistentState = persistentState;
        _httpContextAccessor = httpContextAccessor;

        AuthenticationStateChanged += OnAuthenticationStateChanged;
        _subscription = _persistentState.RegisterOnPersisting(OnPersistingAsync, RenderMode.InteractiveWebAssembly);
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var user = _httpContextAccessor.HttpContext?.User;

        if (user?.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(new AuthenticationState(user));
        }

        return Task.FromResult(new AuthenticationState(new System.Security.Claims.ClaimsPrincipal()));
    }

    private void OnAuthenticationStateChanged(Task<AuthenticationState> task)
    {
        _authStateTask = task;
    }

    private async Task OnPersistingAsync()
    {
        var authState = _authStateTask != null
            ? await _authStateTask
            : await GetAuthenticationStateAsync();

        var principal = authState.User;

        if (principal.Identity?.IsAuthenticated == true)
        {
            var claims = principal.Claims.Select(c => new UserClaimModel
            {
                Type = c.Type,
                Value = c.Value,
            }).ToList();

            _persistentState.PersistAsJson(nameof(UserClaimModel), claims);
        }
    }

    public void Dispose()
    {
        _subscription.Dispose();
        AuthenticationStateChanged -= OnAuthenticationStateChanged;
    }
}
