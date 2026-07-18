using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShiftSoftware.ShiftEntity.Core;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core.DTOs.App;
using ShiftSoftware.ShiftIdentity.Core.Localization;
using ShiftSoftware.ShiftIdentity.Data;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Threading.Tasks;

using ShiftSoftware.ShiftIdentity.Core;

namespace ShiftSoftware.ShiftIdentity.Data.Entities;

// Attribute-driven endpoint: App has no controller. The secure CRUD routes come from the attribute (built-in
// repository + source-generated mapper — App uses AppDTO for both list and view), gated by
// ShiftIdentityActions.Apps. Feature locking is enforced centrally by FeatureLockSaveValidator. The duplicate-AppId
// write check moves onto the entity via IUpsertsShiftRepository. A slim AppRepository survives ONLY to serve
// IAppRepository.GetAppAsync (used by the OAuth AuthCodeService) — it no longer participates in CRUD.
[TemporalShiftEntity]
[Table("Apps", Schema = "ShiftIdentity")]
[ShiftEntitySecureEndpoint<AppDTO, AppDTO, ShiftIdentityActions>("api/IdentityApp", nameof(ShiftIdentityActions.Apps), UseGeneratedMapper = true)]
public class App : ShiftEntity<App>, IUpsertsShiftRepository<App, AppDTO, AppDTO>
{
    [Required]
    [MaxLength(255)]
    public string DisplayName { get; set; } = default!;

    [Required]
    [MaxLength(255)]
    public string AppId { get; set; } = default!;

    [MaxLength(255)]
    public string? AppSecret { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(4000)]
    public string RedirectUri { get; set; } = default!;

    //[Required]
    //[MaxLength(4000)]
    //public string PostLogoutRedirectUri { get; set; } = default!;

    // Genuine write logic from the old AppRepository.UpsertAsync: reject a duplicate AppId (case-insensitive)
    // in the same scope, excluding the current row on update. Runs BEFORE context.Base() (mapping/persist),
    // exactly as the old override ran before base.UpsertAsync.
    public async ValueTask<App> UpsertAsync(
        App entity,
        AppDTO dto,
        ActionTypes actionType,
        long? userId,
        Guid? idempotencyKey,
        bool disableDefaultDataLevelAccess,
        bool disableGlobalFilters,
        ShiftRepositoryUpsertContext<App, AppDTO, AppDTO> context)
    {
        var db = context.Services.GetRequiredService<ShiftIdentityDbContext>();
        var loc = context.Services.GetRequiredService<ShiftIdentityLocalizer>();

        if (await db.Apps.AnyAsync(x => !x.IsDeleted && x.AppId.ToLower() == dto.AppId.ToLower() && x.ID != entity.ID))
            throw new ShiftEntityException(new Message(loc["Duplicate"], loc["The App ID {0} already exists.", dto.AppId]));

        return await context.Base();
    }
}
