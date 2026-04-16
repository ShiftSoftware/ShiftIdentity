using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.Blazor.Providers;

/// <summary>
/// Client-side (WASM) AuthenticationStateProvider that reads claims
/// from PersistentComponentState (persisted by the server during SSR).
/// Used with cookie-based authentication in Blazor Web App.
/// </summary>
public class PersistentCookieAuthStateProvider : AuthenticationStateProvider
{
    private static readonly Task<AuthenticationState> _unauthenticated =
        Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));

    private readonly Task<AuthenticationState> _authState;

    public PersistentCookieAuthStateProvider(PersistentComponentState persistentState)
    {
        if (persistentState.TryTakeFromJson<List<UserClaimModel>>(nameof(UserClaimModel), out var claimModels)
            && claimModels is { Count: > 0 })
        {
            var claims = claimModels.Select(c => new Claim(c.Type, c.Value)).ToList();
            var identity = new ClaimsIdentity(claims, "Cookies");
            _authState = Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
        }
        else
        {
            _authState = _unauthenticated;
        }
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync() => _authState;
}
