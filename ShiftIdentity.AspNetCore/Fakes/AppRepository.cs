using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Repositories;

namespace ShiftSoftware.ShiftIdentity.AspNetCore.Fakes;

public class AppRepository : IAppRepository
{
    public ValueTask<App> CreateAsync(AppDTO dto, long? userId = null)
    {
        throw new NotImplementedException();
    }

    public ValueTask<App> DeleteAsync(App entity, long? userId = null)
    {
        throw new NotImplementedException();
    }

    public Task<App> FindAsync(long id, DateTime? asOf = null, bool ignoreGlobalFilters = false)
    {
        throw new NotImplementedException();
    }

    public Task<App?> GetAppAsync(string appId)
    {
        throw new NotImplementedException();
    }

    public IQueryable<AppDTO> OdataList(bool ignoreGlobalFilters = false)
    {
        throw new NotImplementedException();
    }

    public ValueTask<App> UpdateAsync(App entity, AppDTO dto, long? userId = null)
    {
        throw new NotImplementedException();
    }

    public ValueTask<AppDTO> ViewAsync(App entity)
    {
        throw new NotImplementedException();
    }
}
