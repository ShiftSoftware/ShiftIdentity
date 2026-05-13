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
    Task<CookieChangePasswordResult> ChangePasswordAsync(ChangePasswordDTO dto, HttpContext httpContext);
}

public sealed record CookieChangePasswordResult(bool Succeeded, string? ErrorMessage);
