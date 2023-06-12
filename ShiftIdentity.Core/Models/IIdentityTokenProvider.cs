using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public interface IIdentityTokenProvider
    {
        Task<TokenDTO> GetTokenAsync();
    }
}