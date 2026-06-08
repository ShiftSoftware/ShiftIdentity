using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Components;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// Blazor-Web-App (HttpOnly auth cookie) implementation of <see cref="IAuthRefreshStrategy"/>.
/// Login and logout are not part of this strategy: cookie-mutating operations must originate
/// from a browser HTML form post to land in the user's cookie jar (form-post contract) —
/// login via the static-SSR <c>LoginForm</c> page, logout via <c>POST /api/identity/logout</c>.
/// </summary>
public class CookieRefreshStrategy : IAuthRefreshStrategy
{
    private readonly HttpClient _http;
    private readonly PersistentComponentState _persistentState;

    public CookieRefreshStrategy(
        HttpClient http,
        PersistentComponentState persistentState)
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
}
