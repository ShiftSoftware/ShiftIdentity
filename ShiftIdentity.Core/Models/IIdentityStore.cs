using ShiftSoftware.ShiftIdentity.Core.DTOs;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Models;

public interface IIdentityStore
{
    public Task<TokenDTO> GetTokenAsync();
    public Task StoreTokenAsync(TokenDTO token);
    public Task RemoveTokenAsync();
    public Task StoreCodeVerifierAsync(string code);
    public Task<string> LoadCodeVerifierAsync();
    public Task RemoveCodeVerifierAsync();
}
