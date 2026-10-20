using MudBlazor;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

public class AdditionalTreeItem
{
    public bool Expanded { get; set; }
    public ActionTreeNode? Parent { get; set; }
    public List<Access>? OldWildCardAccess { get; set; }

    // Bumped when a wildcard on this node or above it changes, which is what tells the node's row to render again.
    public int Version { get; set; }

    // Built once, so the tree item gets the same Items on every render and doesn't re-render its children for a new array.
    public IReadOnlyCollection<TreeItemData<ActionTreeNode>> Children { get; set; } = [];

    public AdditionalTreeItem()
    {
    }
}

internal static class ActionTreeNodeExtensions
{
    // ActionTreeNode.AdditionalData is dynamic: reading it through this typed view skips the runtime binder, which every
    // row would otherwise run several times per render.
    public static AdditionalTreeItem Extra(this ActionTreeNode node)
    {
        object? data = node.AdditionalData;
        return (AdditionalTreeItem)data!;
    }

    // A row under a parent that grants wildcard access shows that access and can't be toggled on its own.
    public static List<Access>? ParentWildCardAccess(this ActionTreeNode node) => node.Extra().Parent?.WildCardAccess;
}
