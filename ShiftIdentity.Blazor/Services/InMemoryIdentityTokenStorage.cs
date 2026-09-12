using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor.Services;

/// <summary>Persistence for one server circuit. Register as scoped so circuits never share credentials.</summary>
public sealed class InMemoryIdentityTokenStorage : IIdentityTokenStorage
{
    private TokenDTO? token;
    public TokenDTO? Read() => token;
    public Task<TokenDTO?> ReadAsync() => Task.FromResult(token);
    public Task WriteAsync(TokenDTO value) { token = value; return Task.CompletedTask; }
    public Task RemoveAsync() { token = null; return Task.CompletedTask; }
}
