using ShiftSoftware.ShiftIdentity.Core.Entities;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Repositories
{
    public interface IAppRepository
    {
        Task<App?> GetAppAsync(string appId);
    }
}