using Microsoft.EntityFrameworkCore.ChangeTracking;
using ShiftSoftware.ShiftEntity.EFCore;
using ShiftSoftware.ShiftEntity.Model;
using ShiftSoftware.ShiftIdentity.Core;
using ShiftSoftware.ShiftIdentity.Core.Localization;

namespace ShiftSoftware.ShiftIdentity.Data;

/// <summary>
/// Enforces ShiftIdentity feature locking on every repository save, WITHOUT a per-repository
/// <c>SaveChangesAsync</c> override. Registered as an <see cref="IShiftEntitySaveValidator"/>; the built-in
/// <c>ShiftRepository</c> invokes it just before it persists a unit of work. When any pending row belongs to a
/// feature that is currently locked, the save is aborted with the same localized message the old per-repository
/// overrides used. This is the generalized replacement for the 13 duplicated feature-lock overrides.
/// </summary>
public class FeatureLockSaveValidator : IShiftEntitySaveValidator
{
    private readonly ShiftIdentityFeatureLocking featureLocking;
    private readonly ShiftIdentityLocalizer localizer;

    public FeatureLockSaveValidator(ShiftIdentityFeatureLocking featureLocking, ShiftIdentityLocalizer localizer)
    {
        this.featureLocking = featureLocking;
        this.localizer = localizer;
    }

    public void Validate(IReadOnlyList<EntityEntry> pendingWrites)
    {
        foreach (var entry in pendingWrites)
        {
            // Metadata.ClrType is the model's entity type (never an EF proxy), so the lookup is exact.
            var messageKey = featureLocking.GetLockedMessageKey(entry.Metadata.ClrType);

            if (messageKey is not null)
                throw new ShiftEntityException(new Message(localizer["Error"], localizer[messageKey]));
        }
    }
}
