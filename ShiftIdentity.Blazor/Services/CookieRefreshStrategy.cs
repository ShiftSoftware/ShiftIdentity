using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Blazor-Web-App (HttpOnly auth cookie) implementation of <see cref="IAuthRefreshStrategy"/>.
/// The cookie is the source of truth — refreshing client-side just means polling
/// <c>/api/identity/me</c>. The server's <c>OnValidatePrincipal</c> handles the actual
/// JWT/refresh-token rotation as a side effect of any authenticated request.
/// </summary>
public class CookieRefreshStrategy : IAuthRefreshStrategy
{
    private readonly HttpClient _http;
    private readonly PersistentComponentState _persistentState;

    public CookieRefreshStrategy(HttpClient http, PersistentComponentState persistentState)
    {
        _http = http;
        _persistentState = persistentState;
    }

    public List<UserClaimModel>? GetInitialClaims()
    {
        if (_persistentState.TryTakeFromJson<List<UserClaimModel>>(nameof(UserClaimModel), out var claims)
            && claims is { Count: > 0 })
        {
            return claims;
        }
        return null;
    }

    public async Task<List<UserClaimModel>?> RefreshAsync()
    {
        var response = await _http.GetAsync("/api/identity/me");

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<CookieAuthStateResponse>();
        return data?.Claims;
    }

    public Task<List<UserClaimModel>?> OnLoginCommittedAsync(AuthLoginResult result)
    {
        // Cookie was set server-side during /api/identity/login or /sign-in-with-token.
        // Nothing to store locally — just hand back the claims for the provider.
        return Task.FromResult(result.Claims);
    }

    public async Task OnLogoutAsync()
    {
        try { await _http.PostAsync("/api/identity/logout", null); }
        catch { /* best-effort: cookie may already be invalid; provider will be cleared regardless */ }
    }
}
