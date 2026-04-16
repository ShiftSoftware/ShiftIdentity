using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>
/// No-op IIdentityStore for cookie-based authentication.
/// Token storage is handled by the auth cookie, not localStorage.
/// </summary>
public class NoOpIdentityStore : IIdentityStore
{
    public Task<TokenDTO?> GetTokenAsync() => Task.FromResult<TokenDTO?>(null);
    public string? GetToken() => null;
    public Task StoreTokenAsync(TokenDTO token) => Task.CompletedTask;
    public Task RemoveTokenAsync() => Task.CompletedTask;
}
