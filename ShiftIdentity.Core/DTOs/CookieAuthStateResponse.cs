using System.Collections.Generic;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs;

/// <summary>
/// Server response for <c>GET /api/identity/me</c>. Carries claims so the WASM
/// AuthenticationStateProvider can mirror server-side auth state, plus a
/// RefreshAfterSeconds hint that drives the client's poll cadence. No JWT or
/// refresh token is exposed to the client.
/// </summary>
public class CookieAuthStateResponse
{
    public List<UserClaimModel> Claims { get; set; } = new();

    public int RefreshAfterSeconds { get; set; }
}
