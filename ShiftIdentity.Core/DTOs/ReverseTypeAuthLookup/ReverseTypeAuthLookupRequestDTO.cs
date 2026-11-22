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

    /// <summary>
    /// The value searched for on a text or number action (required for them, ignored otherwise): the lookup returns
    /// whoever holds at least this value by the action's combine rule — a number at least as great, or a list that
    /// covers every entry of it. <see cref="Access"/> is ignored for these actions. A number is read with the
    /// invariant culture ("12.5").
    /// </summary>
    public string? Value { get; set; }
}
