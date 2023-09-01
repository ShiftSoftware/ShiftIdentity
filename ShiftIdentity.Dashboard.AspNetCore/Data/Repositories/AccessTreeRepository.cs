using AutoMapper;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.TypeAuth.AspNetCore.Services;
using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.TypeAuth.Core;
using System.Net;
using ShiftSoftware.ShiftIdentity.Core.Entities;

namespace ShiftSoftware.ShiftIdentity.Dashboard.AspNetCore.Data.Repositories;

public class AccessTreeRepository :
    ShiftRepository<ShiftIdentityDB, AccessTree, AccessTreeDTO, AccessTreeDTO, AccessTreeDTO>,
    IShiftRepositoryAsync<AccessTree, AccessTreeDTO, AccessTreeDTO>
{
    private readonly TypeAuthService typeAuthService;
    public AccessTreeRepository(ShiftIdentityDB db, TypeAuthService typeAuthService, IMapper mapper) : base(db, db.AccessTrees, mapper)
    {
        this.typeAuthService = typeAuthService;
    }

    public override async ValueTask<AccessTree> UpsertAsync(AccessTree entity, AccessTreeDTO dto, ActionTypes actionType, long? userId = null)
    {
        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the access-tree-name in the same scope is duplicate
        if (await db.AccessTrees.AnyAsync(x => x.Name.ToLower() == dto.Name.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message("Duplicate", $"the access tree name ({dto.Name}) already exists."));

        if (entity.BuiltIn)
            throw new ShiftEntityException(new Message("Error", "Built-In Data can't be modified."), (int)HttpStatusCode.Forbidden);

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

        if (actionType == ActionTypes.Update)
        {
            typeAuthContextBuilder_Preserver.AddAccessTree(entity.Tree);
            typeAuth_Preserver = typeAuthContextBuilder_Preserver.Build();
        }

        typeAuth_Producer = typeAuthContextBuilder_Producer.Build();

        entity.Tree = typeAuth_Producer.GenerateAccessTree(typeAuthService, typeAuth_Preserver);

        entity.Name = dto.Name;

        return entity;
    }
}
