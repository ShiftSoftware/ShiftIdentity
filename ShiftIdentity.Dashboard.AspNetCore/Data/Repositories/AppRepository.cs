using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.AspNetCore.IRepositories;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class AppRepository :
    ShiftRepository<ShiftIdentityDB, App>,
    IShiftRepositoryAsync<App, AppDTO, AppDTO>,
    IAppRepository
{

    private readonly ShiftIdentityDB db;
    public AppRepository(ShiftIdentityDB db, IMapper mapper) : base(db, db.Apps, mapper)
    {
        this.db = db;
    }
    public async ValueTask<App> CreateAsync(AppDTO dto, long? userId = null)
    {
        //Check if the App-Id is duplicate
        if (await db.Apps.AnyAsync(x => x.AppId.ToLower() == dto.AppId.ToLower()))
            throw new ShiftEntityException(new Message("Duplicate", $"the App-Id {dto.AppId} is exists"));

        var entity = new App().CreateShiftEntity(userId);

        AssignValues(dto, entity);

        return entity;
    }

    public ValueTask<App> DeleteAsync(App entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<App>(entity);
    }

    public async Task<App> FindAsync(long id, DateTime? asOf = null)
    {
        return await base.FindAsync(id, asOf);
    }

    public IQueryable<AppDTO> OdataList(bool showDeletedRows = false)
    {
        var apps= GetIQueryable(showDeletedRows).AsNoTracking();

        return mapper.ProjectTo<AppDTO>(apps);
    }

    public async ValueTask<App> UpdateAsync(App entity, AppDTO dto, long? userId = null)
    {
        //Check if the App-Id is duplicate
        if (dto.AppId.ToLower() != entity.AppId.ToLower())
            if (await db.Apps.AnyAsync(x => x.AppId.ToLower() == dto.AppId.ToLower()))
                throw new ShiftEntityException(new Message("Duplicate", $"the App-Id {dto.AppId} is exists"));

        entity.UpdateShiftEntity(userId);

        AssignValues(dto, entity);

        return entity;
    }

    public ValueTask<AppDTO> ViewAsync(App entity)
    {
        return new ValueTask<AppDTO>(mapper.Map<AppDTO>(entity));
    }

    private void AssignValues(AppDTO dto, App entity)
    {
        entity.DisplayName = dto.DisplayName;
        entity.AppId = dto.AppId;
        entity.AppSecret = dto.AppSecret;
        entity.Description = dto.Description;
        entity.RedirectUri = dto.RedirectUri;
        //entity.PostLogoutRedirectUri = dto.PostLogoutRedirectUri;
    }

    public async Task<App?> GetAppAsync(string appId)
    {
        return await db.Apps.FirstOrDefaultAsync(x => x.AppId == appId);
    }
}
