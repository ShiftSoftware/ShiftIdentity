using ShiftSoftware.ShiftIdentity.Core.DTOs;

namespace ShiftSoftware.ShiftIdentity.Blazor;

/// <summary>
/// Persists credentials for <see cref="IdentitySession"/>. Implementations only read, write and remove
/// stored values; renewal and session policy belong to the framework.
/// </summary>
public interface IIdentityTokenStorage
{
    TokenDTO? Read();
    Task<TokenDTO?> ReadAsync();
    Task WriteAsync(TokenDTO token);
    Task RemoveAsync();
}
