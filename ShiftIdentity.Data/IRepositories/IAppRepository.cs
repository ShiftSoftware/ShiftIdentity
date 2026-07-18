using ShiftSoftware.ShiftIdentity.Data.Entities;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.IRepositories
{
    public interface IAppRepository
    {
        Task<App?> GetAppAsync(string appId);
    }
}