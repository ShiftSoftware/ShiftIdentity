using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// Pluggable refresh + login/logout side effects for the unified <see cref="AuthSessionService"/>
/// loop. The loop, the in-memory state provider, and the 401 handler are shared; only the
/// per-tick HTTP call and the local storage side effects differ between JWT and cookie auth.
/// </summary>
public interface IAuthRefreshStrategy
{
    /// <summary>
    /// Synchronously available initial claims at app startup — JWT parses them from the
    /// stored token in localStorage, cookie reads them from <c>PersistentComponentState</c>.
    /// Returns null if the user appears to be unauthenticated; the loop will still run and
    /// can transition to authenticated on a successful tick (e.g. after login in another tab).
    /// </summary>
    List<UserClaimModel>? GetInitialClaims();

    /// <summary>
    /// One refresh tick. JWT POSTs <c>/Auth/Refresh</c> and parses the new JWT. Cookie GETs
    /// <c>/api/identity/me</c>; the server's <c>OnValidatePrincipal</c> handles the actual
    /// cookie refresh as a side effect when needed.
    /// </summary>
    /// <returns>
    /// Claims on success. Null when the server has definitively rejected the session
    /// (refresh-token expired, cookie revoked) — the loop will treat this as a logout.
    /// Throws on transient failures (network, 5xx); the loop keeps state and retries next tick.
    /// </returns>
    Task<List<UserClaimModel>?> RefreshAsync();

    /// <summary>
    /// Apply local side effects after a successful login. JWT stores the <see cref="AuthLoginResult.Token"/>
    /// in localStorage and parses claims from it. Cookie does nothing (the cookie is already set
    /// by the server) and just returns <see cref="AuthLoginResult.Claims"/>.
    /// </summary>
    Task<List<UserClaimModel>?> OnLoginCommittedAsync(AuthLoginResult result);

    /// <summary>
    /// Apply local side effects on logout. JWT clears localStorage. Cookie POSTs
    /// <c>/api/identity/logout</c> to clear the cookie.
    /// </summary>
    Task OnLogoutAsync();
}
