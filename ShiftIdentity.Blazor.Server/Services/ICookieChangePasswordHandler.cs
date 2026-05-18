using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Core.DTOs.UserManager;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Performs a password change for the authenticated user (in-process for internal hosting,
/// via HTTP for external hosting) and re-issues the auth cookie with fresh claims so the
/// <c>RequirePasswordChange</c> flag clears immediately.
///
/// Called from the static-SSR <c>ChangePasswordForm</c> page where <see cref="HttpContext"/>
/// is alive and the response hasn't started yet, so a fresh Set-Cookie reaches the browser.
/// </summary>
public interface ICookieChangePasswordHandler
{
    /// <summary>
    /// Voluntary change — caller is fully logged in and must supply their current password.
    /// </summary>
    Task<CookieChangePasswordResult> ChangePasswordAsync(ChangePasswordDTO dto, HttpContext httpContext);

    /// <summary>
    /// Forced change — caller holds a challenge cookie (logged in only enough to change their
    /// password). No current password required because they just authenticated. On success
    /// the cookie is upgraded to a full session cookie.
    /// </summary>
    Task<CookieChangePasswordResult> CompletePasswordChangeAsync(CompletePasswordChangeDTO dto, HttpContext httpContext);
}

public sealed record CookieChangePasswordResult(bool Succeeded, string? ErrorMessage);
