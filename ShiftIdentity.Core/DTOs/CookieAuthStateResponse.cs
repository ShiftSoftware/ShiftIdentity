using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

/// <summary>
/// Server response shape for cookie-auth state endpoints (/api/identity/login, /me, /refresh,
/// /sign-in-with-token). Carries claims so the WASM AuthenticationStateProvider can update
/// in-memory state without a full page reload, plus a RefreshAfterSeconds hint that drives
/// the client's poll cadence. No JWT or refresh token is exposed to the client.
/// </summary>
public class CookieAuthStateResponse
{
    /// <summary>
    /// Populated on /login and /sign-in-with-token. Null on /me and /refresh.
    /// </summary>
    public TokenUserDataDTO? UserData { get; set; }

    /// <summary>
    /// Populated on /login and /sign-in-with-token. Always false on /me and /refresh.
    /// </summary>
    public bool RequirePasswordChange { get; set; }

    /// <summary>
    /// Current claims from HttpContext.User after sign-in / refresh. Used by the WASM
    /// PersistentCookieAuthStateProvider to update the AuthenticationState.
    /// </summary>
    public List<UserClaimModel> Claims { get; set; } = new();

    /// <summary>
    /// How many seconds the client should wait before polling /me again. Computed so the
    /// next poll lands inside OnValidatePrincipal's refresh window.
    /// </summary>
    public int RefreshAfterSeconds { get; set; }
}
