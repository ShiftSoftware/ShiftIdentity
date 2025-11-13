using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class AppRepository :
    ShiftRepository<ShiftIdentityDbContext, App, AppDTO, AppDTO>,
    IAppRepository
{

    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;
    public AppRepository(ShiftIdentityDbContext db, ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override async ValueTask<App> UpsertAsync(App entity, AppDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null)
    {
        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the access-tree-name in the same scope is duplicate
        if (await db.Apps.AnyAsync(x => !x.IsDeleted && x.AppId.ToLower() == dto.AppId.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["The App ID {0} already exists.", dto.AppId]));

        return await base.UpsertAsync(entity, dto, actionType, userId);
    }

    public async Task<App?> GetAppAsync(string appId)
    {
        return await db.Apps.FirstOrDefaultAsync(x => x.AppId == appId && !x.IsDeleted);
    }

    public override Task<int> SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.AppFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["App Feature is locked"]));

        return base.SaveChangesAsync();
    }
}
