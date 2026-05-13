using Microsoft.AspNetCore.Http;
using ShiftSoftware.ShiftIdentity.AspNetCore.Models;
using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Blazor.Server.Helpers;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Authenticates against the in-process <see cref="AuthService"/> (internal identity hosting).
/// </summary>
internal sealed class InternalCookieLoginHandler : ICookieLoginHandler
{
    private readonly AuthService _authService;

    public InternalCookieLoginHandler(AuthService authService)
    {
        _authService = authService;
    }

    public async Task<CookieLoginResult> LoginAsync(LoginDTO loginDto, HttpContext httpContext)
    {
        var result = await _authService.LoginAsync(loginDto);
        if (result.Result != LoginResultEnum.Success)
            return new CookieLoginResult(false, result.ErrorMessage, false);

        await CookieAuthHelpers.SignInWithToken(httpContext, result.Token);
        return new CookieLoginResult(true, null, result.Token.RequirePasswordChange);
    }
}
