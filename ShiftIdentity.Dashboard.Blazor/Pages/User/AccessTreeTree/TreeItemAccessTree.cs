using ShiftSoftware.ShiftIdentity.Core.DTOs.AccessTree;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User.AccessTreeTree;

public class TreeItemAccessTree
{
    public TreeItemAccessTree? Parent { get; set; } = null;

    public string Text { get; set; }

    public bool IsExpanded { get; set; } = false;

    public bool IsChecked { get; set; } = false;

    public bool HasChild => TreeItems != null && TreeItems.Count > 0;

    public AccessTreeDTO? AccessTree { get; set; }

    public HashSet<TreeItemAccessTree> TreeItems { get; set; } = new HashSet<TreeItemAccessTree>();

    public TreeItemAccessTree(string text)
    {
        Text = text;
    }

    public TreeItemAccessTree(string text, AccessTreeDTO accessTree)
    {
        Text = text;
        AccessTree = accessTree;
    }

    public TreeItemAccessTree AddChild(string itemName)
    {
        TreeItemAccessTree item = new TreeItemAccessTree(itemName);
        item.Parent = this;
        TreeItems.Add(item);
        return item;
    }

    public TreeItemAccessTree AddChild(string itemName, AccessTreeDTO accessTree)
    {
        TreeItemAccessTree item = new TreeItemAccessTree(itemName, accessTree);
        item.Parent = this;
        TreeItems.Add(item);
        return item;
    }

    public bool HasPartialChildSelection()
    {
        int iChildrenCheckedCount = (from c in TreeItems where c.IsChecked select c).Count();
        return HasChild && iChildrenCheckedCount > 0 && iChildrenCheckedCount < TreeItems.Count();
    }

    public static void CheckedChanged(TreeItemAccessTree item)
    {
        item.IsChecked = !item.IsChecked;
        // checked status on any child items should mirrror this parent item
        if (item.HasChild)
        {
            foreach (TreeItemAccessTree child in item.TreeItems)
            {
                child.IsChecked = item.IsChecked;
            }
        }
        // if there's a parent and all children are checked/unchecked, parent should match
        if (item.Parent != null)
        {
            item.Parent.IsChecked = !item.Parent.TreeItems.Any(i => !i.IsChecked);
        }
    }
}
