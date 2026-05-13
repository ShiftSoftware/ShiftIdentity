using Microsoft.AspNetCore.Components.Authorization;
using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Security.Claims;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// In-memory <see cref="AuthenticationStateProvider"/> shared by JWT (standalone WASM) and
/// cookie (Blazor Web App) auth. The state is mutated by <see cref="AuthSessionService"/>
/// after the initial seed, refresh ticks, login, and logout — each call raises
/// <c>AuthenticationStateChanged</c> so cascading consumers re-evaluate without a full reload.
/// </summary>
public class ShiftAuthStateProvider : AuthenticationStateProvider
{
    private const string AuthenticationType = "ShiftAuth";

    private static readonly AuthenticationState _unauthenticated =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    private AuthenticationState _authState = _unauthenticated;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(_authState);

    public void NotifyUserAuthentication(IEnumerable<UserClaimModel> claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            AuthenticationType);
        _authState = new AuthenticationState(new ClaimsPrincipal(identity));
        NotifyAuthenticationStateChanged(Task.FromResult(_authState));
    }

    public void NotifyUserLoggedOut()
    {
        _authState = _unauthenticated;
        NotifyAuthenticationStateChanged(Task.FromResult(_authState));
    }
}
