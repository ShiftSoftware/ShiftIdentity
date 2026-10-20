using System.Security.Claims;
using AngleSharp.Dom;
using Bunit;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;
using ActionPickedEventArgs = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree.ActionPickedEventArgs;
using ActionTreeComponent = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree.ActionTree;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The action tree renders a folder's rows only while the folder is expanded: MudBlazor renders a collapsed item's
/// children too, only hidden, and with the data-level lists expanded a host's tree holds hundreds of rows.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class ActionTreeExpansionTests
{
    // The admin holds the Read wildcard on Reports, and nothing else.
    private const string Admin = """{"ExpansionTestActions":{"Reports":["r"]}}""";

    [Fact]
    public async Task A_folder_renders_its_rows_only_while_expanded_and_keeps_what_was_open_inside_it()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Admin, actionTrees: typeof(ExpansionTestActions));
        var cut = Render(context);

        Assert.Null(ActionTreeRows.Find(cut, "Orders"));

        ActionTreeRows.Expand(cut, "Expansion Test Actions", "Orders");
        Assert.Null(ActionTreeRows.Find(cut, "Sales"));

        ActionTreeRows.Expand(cut, "Reports", "Sales");

        ActionTreeRows.Collapse(cut, "Expansion Test Actions", "Orders");
        Assert.Null(ActionTreeRows.Find(cut, "Sales"));

        // Reports was left open inside, so reopening the folder shows Sales again.
        ActionTreeRows.Expand(cut, "Expansion Test Actions", "Sales");
    }

    [Fact]
    public async Task A_folder_opens_from_its_arrow_and_a_click_on_its_access_buttons_leaves_it_as_it_is()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Admin, actionTrees: typeof(ExpansionTestActions));
        var cut = Render(context);

        Arrow(cut, "Expansion Test Actions")!.Click();
        cut.WaitForAssertion(() => Assert.NotNull(ActionTreeRows.Find(cut, "Orders")));
        Assert.Null(Arrow(cut, "Orders"));

        Button(cut, "Reports", "Read").Click();
        Assert.Null(ActionTreeRows.Find(cut, "Sales"));
    }

    // The expand arrow in front of a row, which only a folder has.
    private static IElement? Arrow(IRenderedComponent<ActionTreeComponent> cut, string row) =>
        ActionTreeRows.Row(cut, row).Closest(".mud-treeview-item-content")!.QuerySelector(".mud-treeview-item-arrow button");

    [Fact]
    public async Task A_wildcard_is_offered_only_where_the_admin_holds_it_and_covers_the_rows_below()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Admin, actionTrees: typeof(ExpansionTestActions));
        var cut = Render(context);
        ActionTreeRows.Expand(cut, "Expansion Test Actions", "Reports");
        ActionTreeRows.Expand(cut, "Reports", "Sales");

        Assert.True(Button(cut, "Expansion Test Actions", "Read").HasAttribute("disabled"));
        Assert.True(Button(cut, "Reports", "Write").HasAttribute("disabled"));
        Assert.False(Button(cut, "Reports", "Read").HasAttribute("disabled"));
        Assert.DoesNotContain("action-tree-row-inherited", ActionTreeRows.Row(cut, "Sales").ClassName);

        Button(cut, "Reports", "Read").Click();

        cut.WaitForAssertion(() => Assert.Contains("action-tree-row-inherited", ActionTreeRows.Row(cut, "Sales").ClassName));
        Assert.True(Button(cut, "Sales", "Read").HasAttribute("disabled"));
    }

    [Fact]
    public async Task The_rows_follow_the_admins_rights_when_they_are_rebuilt()
    {
        await using var context = UserFormHarness.Create(out _, out var authorization, accessTree: Admin, actionTrees: typeof(ExpansionTestActions));
        var cut = Render(context);
        ActionTreeRows.Expand(cut, "Expansion Test Actions", "Reports");
        ActionTreeRows.Expand(cut, "Reports", "Sales");
        Assert.True(Button(cut, "Reports", "Write").HasAttribute("disabled"));
        Assert.True(Button(cut, "Orders", "Write").HasAttribute("disabled"));

        // A token refresh brings the admin Write on Reports and on Orders.
        authorization.SetClaims(new Claim(TypeAuthClaimTypes.AccessTree, """{"ExpansionTestActions":{"Reports":["r","w"],"Orders":["w"]}}"""));

        cut.WaitForAssertion(() => Assert.False(Button(cut, "Reports", "Write").HasAttribute("disabled")));
        Assert.False(Button(cut, "Orders", "Write").HasAttribute("disabled"));
    }

    [Fact]
    public async Task In_picker_mode_a_row_reports_the_action_and_access_picked()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Admin, actionTrees: typeof(ExpansionTestActions));
        ActionPickedEventArgs? picked = null;
        var cut = Render(context, p => p.Add(x => x.PickerMode, true).Add(x => x.OnActionPicked, (ActionPickedEventArgs x) => picked = x));
        ActionTreeRows.Expand(cut, "Expansion Test Actions", "Orders");

        Button(cut, "Orders", "Write").Click();

        Assert.NotNull(picked);
        Assert.Equal(ExpansionTestActions.Orders.Path, picked!.ActionPath);
        Assert.Equal(Access.Write, picked.Access);
        Assert.False(picked.AnyDynamicRow);
        Assert.Equal("Orders · Write", picked.DisplayName);
    }

    private static IRenderedComponent<ActionTreeComponent> Render(BunitContext context, Action<ComponentParameterCollectionBuilder<ActionTreeComponent>>? parameters = null)
    {
        var cut = context.Render<ActionTreeComponent>(p => { p.Add(x => x.Tree, "{}"); parameters?.Invoke(p); });
        cut.WaitForAssertion(() => Assert.NotNull(ActionTreeRows.Find(cut, "Expansion Test Actions")));
        return cut;
    }

    private static IElement Button(IRenderedComponent<ActionTreeComponent> cut, string row, string label) =>
        ActionTreeRows.Row(cut, row).QuerySelectorAll("button").Single(x => x.TextContent.Trim() == label);

    [ActionTree("Expansion Test Actions", "")]
    public class ExpansionTestActions
    {
        public static readonly ReadWriteDeleteAction Orders = new("Orders");

        [ActionTree("Reports", "")]
        public class Reports
        {
            public static readonly ReadAction Sales = new("Sales");
        }
    }
}
