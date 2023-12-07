using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftEntity.Model.Extensions;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.IRepositories;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class AppRepository :
    ShiftRepository<ShiftIdentityDbContext, App, AppDTO, AppDTO>,
    IAppRepository
{

    public AppRepository(ShiftIdentityDbContext db, IMapper mapper) : base(db)
    {
    }

    public override async ValueTask<App> UpsertAsync(App entity, AppDTO dto, ActionTypes actionType, long? userId = null)
    {
        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the access-tree-name in the same scope is duplicate
        if (await db.Apps.AnyAsync(x => !x.IsDeleted && x.AppId.ToLower() == dto.AppId.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message("Duplicate", $"The App ID ({dto.AppId}) already exists."));

        return await base.UpsertAsync(entity, dto, actionType, userId);
    }

    public async Task<App?> GetAppAsync(string appId)
    {
        return await db.Apps.FirstOrDefaultAsync(x => x.AppId == appId && !x.IsDeleted);
    }
}
