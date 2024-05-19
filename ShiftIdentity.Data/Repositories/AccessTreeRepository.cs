using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class AccessTreeRepository : ShiftRepository<ShiftIdentityDbContext, AccessTree, AccessTreeDTO, AccessTreeDTO>
{
    private readonly ITypeAuthService typeAuthService;
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;

    public AccessTreeRepository(ShiftIdentityDbContext db, ITypeAuthService typeAuthService, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking) : base(db)
    {
        this.typeAuthService = typeAuthService;
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
    }

    public override async ValueTask<AccessTree> UpsertAsync(AccessTree entity, AccessTreeDTO dto, ActionTypes actionType, long? userId = null)
    {
        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the access-tree-name in the same scope is duplicate
        if (await db.AccessTrees.AnyAsync(x => !x.IsDeleted && x.Name.ToLower() == dto.Name.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message("Duplicate", $"the access tree name ({dto.Name}) already exists."));

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

        entity.Tree = typeAuth_Producer.GenerateAccessTree((typeAuthService as TypeAuthContext)!, typeAuth_Preserver);

        entity.Name = dto.Name;

        return entity;
    }

    public override Task SaveChangesAsync(bool raiseBeforeCommitTriggers = false)
    {
        if (shiftIdentityFeatureLocking.AccessTreeFeatureIsLocked)
            throw new ShiftEntityException(new Message("Error", "Access Tree Feature is locked"));

        return base.SaveChangesAsync(raiseBeforeCommitTriggers);
    }
}