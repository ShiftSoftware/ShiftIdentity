using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

public class ActionPickedEventArgs
{
    public string ActionPath { get; set; } = default!;
    public Access Access { get; set; }
    public string? DynamicId { get; set; }
    public bool AnyDynamicRow { get; set; }
    public string DisplayName { get; set; } = default!;
}
