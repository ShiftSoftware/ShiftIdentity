using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

public class AdditionalTreeItem
{
    public bool Expanded { get; set; }
    public ActionTreeItem? Parent { get; set; }
    public List<Access>? OldWildCardAccess { get; set; }

    public AdditionalTreeItem()
    {
    }
}
