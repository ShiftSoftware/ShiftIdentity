using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Authenticates the user (in-process for internal hosting, via HTTP for external hosting)
/// and writes the auth cookie onto the supplied <see cref="HttpContext"/>.
///
/// Called from the static-SSR <c>LoginForm</c> page where <see cref="HttpContext"/> is alive
/// and the response hasn't started yet, so <see cref="Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignInAsync(HttpContext, System.Security.Claims.ClaimsPrincipal)"/> can write Set-Cookie headers
/// that actually reach the browser.
/// </summary>
public interface ICookieLoginHandler
{
    Task<CookieLoginResult> LoginAsync(LoginDTO loginDto, HttpContext httpContext);
}

public sealed record CookieLoginResult(bool Succeeded, string? ErrorMessage, bool RequirePasswordChange);
