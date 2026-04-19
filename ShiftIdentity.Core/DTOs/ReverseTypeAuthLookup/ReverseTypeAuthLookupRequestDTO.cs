using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Core.DTOs.ReverseTypeAuthLookup;

public class ReverseTypeAuthLookupRequestDTO
{
    /// <summary>
    /// Dotted path that identifies the action in the registered action trees.
    /// Matches <see cref="ShiftSoftware.TypeAuth.Core.Actions.ActionBase.Path"/>.
    /// Example: "ShiftIdentityActions.Users" or "ShiftIdentityActions.DataLevelAccess.Branches".
    /// </summary>
    public string ActionPath { get; set; } = default!;

    public Access Access { get; set; } = Access.Read;

    /// <summary>
    /// Row id for dynamic actions. Ignored for non-dynamic actions.
    /// </summary>
    public string? DynamicId { get; set; }

    /// <summary>
    /// When true and the action is dynamic, returns users who have the requested
    /// access on any row (wildcard or at least one specific row).
    /// DynamicId is ignored when this is true.
    /// </summary>
    public bool AnyDynamicRow { get; set; }
}
