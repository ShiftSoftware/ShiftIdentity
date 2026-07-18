using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftIdentity.Data.Entities;
using ShiftSoftware.ShiftIdentity.Data.IRepositories;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

// App's CRUD is attribute-driven now (built-in repository + source-generated mapper; the duplicate-AppId check
// lives on the App entity's IUpsertsShiftRepository hook, and feature locking on FeatureLockSaveValidator). This
// slim repository survives ONLY to implement IAppRepository for the OAuth AuthCodeService (GetAppAsync) — it is no
// longer a ShiftRepository and no longer participates in the CRUD pipeline, so there's no repository-for-the-same-
// triple collision with the built-in/attribute endpoint.
public class AppRepository : IAppRepository
{
    private readonly ShiftIdentityDbContext db;

    public AppRepository(ShiftIdentityDbContext db)
    {
        this.db = db;
    }

    public Task<App?> GetAppAsync(string appId)
        => db.Apps.FirstOrDefaultAsync(x => x.AppId == appId && !x.IsDeleted);
}
