using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Models
{

    public interface IIdentityTokenStore
    {
        Task StoreTokenAsync(TokenDTO token);
    }
}