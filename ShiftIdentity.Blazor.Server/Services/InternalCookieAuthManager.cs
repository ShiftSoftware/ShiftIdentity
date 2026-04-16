using ShiftSoftware.ShiftIdentity.AspNetCore.Services;
using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Refreshes tokens via in-process AuthService (internal identity hosting).
/// </summary>
public class InternalCookieAuthManager : ICookieAuthManager
{
    private readonly AuthService _authService;

    public InternalCookieAuthManager(AuthService authService)
    {
        _authService = authService;
    }

    public async Task<TokenDTO?> RefreshAsync(string refreshToken)
    {
        return await _authService.RefreshAsync(refreshToken);
    }
}
