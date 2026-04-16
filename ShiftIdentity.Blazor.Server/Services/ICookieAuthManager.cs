using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Server.Services;

/// <summary>
/// Abstracts token refresh for both internal and external hosting.
/// Internal hosting refreshes in-process via AuthService; external hosting calls the identity server via HTTP.
/// </summary>
public interface ICookieAuthManager
{
    Task<TokenDTO?> RefreshAsync(string refreshToken);
}
