using Microsoft.EntityFrameworkCore;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Entities;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Repositories;

public class AccessTreeRepository : ShiftRepository<ShiftIdentityDbContext, AccessTree, AccessTreeListDTO, AccessTreeDTO>
{
    private readonly ITypeAuthService typeAuthService;
    private readonly ShiftIdentityFeatureLocking shiftIdentityFeatureLocking;
    private readonly ShiftIdentityLocalizer Loc;

    public AccessTreeRepository(ShiftIdentityDbContext db, ShiftIdentityDefaultDataLevelAccessOptions shiftIdentityDefaultDataLevelAccessOptions, ITypeAuthService typeAuthService, ShiftIdentityFeatureLocking shiftIdentityFeatureLocking, ShiftIdentityLocalizer Loc) : base(db)
    {
        this.typeAuthService = typeAuthService;
        this.shiftIdentityFeatureLocking = shiftIdentityFeatureLocking;
        this.Loc = Loc;
        this.ShiftRepositoryOptions.DefaultDataLevelAccessOptions = shiftIdentityDefaultDataLevelAccessOptions;
    }

    public override async ValueTask<AccessTree> UpsertAsync(AccessTree entity, AccessTreeDTO dto, ActionTypes actionType, long? userId = null, Guid? idempotencyKey = null, bool disableDefaultDataLevelAccess = false)
    {
        long id = 0;

        if (actionType == ActionTypes.Update)
            id = dto.ID!.ToLong();

        //Check if the access-tree-name in the same scope is duplicate
        if (await db.AccessTrees.AnyAsync(x => !x.IsDeleted && x.Name.ToLower() == dto.Name.ToLower() && x.ID != id))
            throw new ShiftEntityException(new Message(Loc["Duplicate"], Loc["the access tree name {0} already exists.", dto.Name]));

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

    public override Task<int> SaveChangesAsync()
    {
        if (shiftIdentityFeatureLocking.AccessTreeFeatureIsLocked)
            throw new ShiftEntityException(new Message(Loc["Error"], Loc["Access Tree Feature is locked"]));

        return base.SaveChangesAsync();
    }
}