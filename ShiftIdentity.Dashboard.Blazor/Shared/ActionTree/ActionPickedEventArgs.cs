using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

public class ActionPickedEventArgs
{
    public string ActionPath { get; set; } = default!;
    public Access Access { get; set; }
    public string? DynamicId { get; set; }
    public bool AnyDynamicRow { get; set; }
    public string DisplayName { get; set; } = default!;

    /// <summary>
    /// A text or number action, which is searched by a value rather than an access level: the host asks for the
    /// <see cref="Value"/> before searching. <see cref="Access"/> means nothing for it.
    /// </summary>
    public bool RequiresValue { get; set; }

    /// <summary>A number (decimal) action: its value is a number, read with the invariant culture.</summary>
    public bool IsNumber { get; set; }

    /// <summary>The action's value for no access, if it declares one.</summary>
    public string? MinimumValue { get; set; }

    /// <summary>The action's value for full access, if it declares one.</summary>
    public string? MaximumValue { get; set; }

    /// <summary>The value searched for on a text or number action; the host sets it.</summary>
    public string? Value { get; set; }
}
