using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Core.Repositories
{
    public interface IAppRepository
    {
        ValueTask<App> CreateAsync(AppDTO dto, long? userId = null);
        ValueTask<App> DeleteAsync(App entity, long? userId = null);
        Task<App> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false);
        Task<App?> GetAppAsync(string appId);
        IQueryable<AppDTO> OdataList(bool ignoreGlobalFilters = false);
        ValueTask<App> UpdateAsync(App entity, AppDTO dto, long? userId = null);
        ValueTask<AppDTO> ViewAsync(App entity);
    }
}