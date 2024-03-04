using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Blazor;

public interface IIdentityStore
{
    public Task<TokenDTO?> GetTokenAsync();
    public string? GetToken();
    public Task StoreTokenAsync(TokenDTO token);
    public Task RemoveTokenAsync();
}
