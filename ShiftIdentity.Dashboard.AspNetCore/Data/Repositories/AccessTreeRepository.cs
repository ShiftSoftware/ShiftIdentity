using AutoMapper;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.EFCore.SqlServer;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.AspNetCore.Entities;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.TypeAuth.AspNetCore.Services;
using ShiftSoftware.TypeAuth.Core;
using System.Net;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class AccessTreeRepository : ShiftRepository<ShiftIdentityDB, AccessTree>,
    IShiftRepositoryAsync<AccessTree, AccessTreeDTO, AccessTreeDTO>
{

    private readonly ShiftIdentityDB db;
    private readonly TypeAuthService typeAuthService;
    public AccessTreeRepository(ShiftIdentityDB db, TypeAuthService typeAuthService, IMapper mapper) : base(db, db.AccessTrees, mapper)
    {
        this.db = db;
        this.typeAuthService = typeAuthService;
    }
    public async ValueTask<AccessTree> CreateAsync(AccessTreeDTO dto, long? userId = null)
    {
        //Check if the access-tree-name in the same scope is duplicate
        if (await db.AccessTrees.AnyAsync(x => x.Name.ToLower() == dto.Name.ToLower()))
            throw new ShiftEntityException(new Message("Duplicate", $"the scope name {dto.Name} is exists for this scope"));

        var entity = new AccessTree().CreateShiftEntity(userId);

        AssignValues(dto, entity);

        return entity;
    }

    public ValueTask<AccessTree> DeleteAsync(AccessTree entity, long? userId = null)
    {
        entity.DeleteShiftEntity(userId);
        return new ValueTask<AccessTree>(entity);
    }

    public async Task<AccessTree> FindAsync(long id, DateTime? asOf = null)
    {
        return await base.FindAsync
            (id, asOf);
    }

    public IQueryable<AccessTreeDTO> OdataList(bool showDeletedRows = false)
    {
        IQueryable<AccessTree> trees= GetIQueryable(showDeletedRows).AsNoTracking();

        return mapper.ProjectTo<AccessTreeDTO>(trees);
    }

    public async ValueTask<AccessTree> UpdateAsync(AccessTree entity, AccessTreeDTO dto, long? userId = null)
    {
        //Check if the access-tree-name in the same scope is duplicate
        if (dto.Name.ToLower() != entity.Name.ToLower())
            if (await db.AccessTrees.AnyAsync(x => x.Name.ToLower() == dto.Name.ToLower()))
                throw new ShiftEntityException(new Message("Duplicate", $"the scope name {dto.Name} is exists"));

        entity.UpdateShiftEntity(userId);

        AssignValues(dto, entity);

        return entity;
    }

    public ValueTask<AccessTreeDTO> ViewAsync(AccessTree entity)
    {
        return new ValueTask<AccessTreeDTO>(mapper.Map<AccessTreeDTO>(entity));
    }

    private void AssignValues(AccessTreeDTO dto, AccessTree entity)
    {
        var typeAuthContextBuilder_Producer = new TypeAuthContextBuilder();
        var typeAuthContextBuilder_Preserver = new TypeAuthContextBuilder();

        TypeAuthContext typeAuth_Producer;
        TypeAuthContext? typeAuth_Preserver = null;

        foreach (var type in typeAuthService.GetRegisteredActionTrees())
        {
            typeAuthContextBuilder_Producer.AddActionTree(type);
            typeAuthContextBuilder_Preserver.AddActionTree(type);
        }

        typeAuthContextBuilder_Producer.AddAccessTree(dto.Tree);

        if (entity.ID != default)
        {
            typeAuthContextBuilder_Preserver.AddAccessTree(entity.Tree);
            typeAuth_Preserver = typeAuthContextBuilder_Preserver.Build();
        }

        typeAuth_Producer = typeAuthContextBuilder_Producer.Build();

        entity.Tree = typeAuth_Producer.GenerateAccessTree(typeAuthService, typeAuth_Preserver);

        entity.Name = dto.Name;

        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);
    }
}
