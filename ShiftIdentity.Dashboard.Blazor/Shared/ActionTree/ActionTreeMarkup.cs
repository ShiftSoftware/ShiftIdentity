using Microsoft.AspNetCore.Components;
using MudBlazor;
using ShiftSoftware.TypeAuth.Core;

namespace ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree;

/// <summary>
/// The markup MudButton, MudIcon and MudText render for the action tree, written out directly. A tree row holds up to four
/// access buttons and the trees hold hundreds of rows, and each MudButton is three components (MudButton, MudElement,
/// MudIcon), which made a row about fifteen components. The classes are MudBlazor's own, so the rows look the same.
/// </summary>
internal static class ActionTreeMarkup
{
    private static readonly MarkupString Checked = Svg(Icons.Material.Filled.CheckBox, "mud-icon-size-small");
    private static readonly MarkupString Unchecked = Svg(Icons.Material.Filled.CheckBoxOutlineBlank, "mud-icon-size-small");

    private static readonly MarkupString FolderIcon = Svg(Icons.Material.Filled.Folder, "mud-primary-text mud-icon-size-medium ml-0 mr-2");
    private static readonly MarkupString BooleanIcon = Svg(Icons.Material.Outlined.ToggleOff, "mud-icon-size-small ml-0 mr-2", "opacity:0.5;");
    private static readonly MarkupString TextIcon = Svg(Icons.Material.Outlined.TextFields, "mud-icon-size-small ml-0 mr-2", "opacity:0.5;");
    private static readonly MarkupString ReadIcon = Svg(Icons.Material.Outlined.RemoveRedEye, "mud-icon-size-small ml-0 mr-2", "opacity:0.5;");
    private static readonly MarkupString ListIcon = Svg(Icons.Material.Outlined.Checklist, "mud-icon-size-small ml-0 mr-2", "opacity:0.5;");

    /// <summary>The class MudButton gives a small button of this color, filled when checked.</summary>
    public static string ButtonClass(bool isChecked, Color color, string? extraClass = null)
    {
        var variant = isChecked ? "filled" : "text";
        var colorName = color.ToString().ToLowerInvariant();
        var css = $"mud-button-root mud-button mud-button-{variant} mud-button-{variant}-{colorName} mud-button-{variant}-size-small mud-ripple";
        return string.IsNullOrEmpty(extraClass) ? css : $"{css} {extraClass}";
    }

    /// <summary>The checkbox icon a MudButton shows as its start icon.</summary>
    public static MarkupString CheckBox(bool isChecked) => isChecked ? Checked : Unchecked;

    /// <summary>The icon in front of a row: a folder for a group, otherwise the action's type.</summary>
    public static MarkupString ItemIcon(ActionTreeNode item)
    {
        if (item.ActionTreeItems.Count > 0)
            return FolderIcon;

        return item.Action?.Type switch
        {
            ActionType.Boolean => BooleanIcon,
            ActionType.Text => TextIcon,
            ActionType.Read => ReadIcon,
            ActionType.ReadWrite or ActionType.ReadWriteDelete => ListIcon,
            _ => default,
        };
    }

    private static MarkupString Svg(string icon, string css, string? style = null) => new(
        $"<svg class=\"mud-icon-root mud-svg-icon {css}\"{(style is null ? "" : $" style=\"{style}\"")} focusable=\"false\" viewBox=\"0 0 24 24\" aria-hidden=\"true\" role=\"img\">{icon}</svg>");
}
