using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.AuthRefresh;

/// <summary>
/// Pluggable claims-source behavior for the unified <see cref="AuthSessionService"/> polling loop.
/// JWT (standalone WASM) and cookie (Blazor Web App) modes differ only in where the current
/// claims come from at app startup and on each refresh tick — login and logout side effects
/// are mode-specific and live outside this interface (JWT login in <c>JwtRefreshStrategy</c>,
/// cookie login in the static-SSR <c>LoginForm</c> page; JWT logout cleanup at the call sites,
/// cookie logout in the form-post endpoint).
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
}
