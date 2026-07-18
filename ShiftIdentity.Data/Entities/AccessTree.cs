using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using ShiftSoftware.TypeAuth.Core;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint (Rung B): AccessTree has no controller and no repository class. The secure CRUD
// routes come from the attribute (built-in repository + source-generated mapper), gated by
// ShiftIdentityActions.AccessTrees; feature locking is central (FeatureLockSaveValidator). The only write logic —
// name-uniqueness + the TypeAuth access-tree generation — moves onto the entity via IUpsertsShiftRepository.
[TemporalShiftEntity]
[Table("AccessTrees", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<AccessTreeListDTO, AccessTreeDTO, ShiftIdentityActions>("api/IdentityAccessTree", nameof(ShiftIdentityActions.AccessTrees), UseGeneratedMapper = true)]
public class AccessTree : ShiftEntity<AccessTree>, IUpsertsShiftRepository<AccessTree, AccessTreeListDTO, AccessTreeDTO>
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = default!;

    [Required]
    public string Tree { get; set; } = default!;

    public AccessTree()
    {
    }

    public async ValueTask<AccessTree> UpsertAsync(
        AccessTree entity,
        AccessTreeDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<AccessTree, AccessTreeListDTO, AccessTreeDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();
        var loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();
        var typeAuthService = context.Services.GetRequiredService<ITypeAuthService>();

        // Name-uniqueness in scope, excluding the current row (entity.ID == 0 on insert, the loaded id on update).
        if (await db.AccessTrees.AnyAsync(x => !x.IsDeleted && x.Name.ToLower() == dto.Name.ToLower() && x.ID != entity.ID))
            throw new ShiftEntityException(new Message(loc["Duplicate"], loc["the access tree name {0} already exists.", dto.Name]));

        // TypeAuth access-tree generation — computed BEFORE Base(): on update the preserver reads the loaded
        // entity.Tree, which MapToEntity (inside Base()) would overwrite with the raw dto.Tree.
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

        var generatedTree = typeAuth_Producer.GenerateAccessTree((typeAuthService as TypeAuthContext)!, typeAuth_Preserver);

        // Framework default: MapToEntity (dto.Name→Name, dto.Tree→Tree), audit stamping, data-level write check.
        var saved = await context.Base();

        // Overwrite the convention-mapped raw dto.Tree with the generated tree (must be after Base()).
        saved.Tree = generatedTree;

        return saved;
    }
}
