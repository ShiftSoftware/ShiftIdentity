using Bunit;
using MudBlazor;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;
using ActionPickedEventArgs = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree.ActionPickedEventArgs;
using ActionTreeComponent = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree.ActionTree;
using NavMenu = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.NavMenu;
using ReverseTypeAuthLookupPage = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.ReverseTypeAuthLookup.ReverseTypeAuthLookup;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The reverse lookup searches a text or number action by a value: the tree offers "Search by value", the page asks for
/// the value and sends it with the lookup.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class ReverseTypeAuthLookupPageTests
{
    [Fact]
    public async Task A_number_action_is_searched_by_the_value_entered()
    {
        await using var context = UserFormHarness.Create(out var transport, accessTree: Auditor, actionTrees: typeof(LookupPageActions));
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<ReverseTypeAuthLookupPage>();
        cut.WaitForAssertion(() => Assert.NotNull(ActionTreeRows.Find(cut, "Lookup Page Actions")));
        ActionTreeRows.Expand(cut, "Lookup Page Actions", "Discount");

        ActionTreeRows.Row(cut, "Discount").QuerySelectorAll("button").Single(x => x.TextContent.Trim() == "Search by value").Click();

        dialogs.WaitForElement("[data-testid=reverse-lookup-search]");
        Assert.True(dialogs.Find("[data-testid=reverse-lookup-search]").HasAttribute("disabled"));
        Assert.Contains("Minimum: 0", dialogs.Markup);
        Assert.Contains("Maximum: 100", dialogs.Markup);

        var input = dialogs.Find("input");
        Assert.Equal("number", input.GetAttribute("type"));
        input.Input("12.5");
        dialogs.Find("[data-testid=reverse-lookup-search]").Click();

        cut.WaitForAssertion(() => Assert.Contains(transport.Gets, x => x.StartsWith("/ReverseTypeAuthLookup?", StringComparison.Ordinal)));
        var lookup = transport.Gets.Single(x => x.StartsWith("/ReverseTypeAuthLookup?", StringComparison.Ordinal));
        Assert.Contains($"ActionPath={Uri.EscapeDataString(LookupPageActions.Discount.Path!)}", lookup, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Value=12.5", lookup);
    }

    [Fact]
    public async Task Cancelling_the_value_searches_nothing()
    {
        await using var context = UserFormHarness.Create(out var transport, accessTree: Auditor, actionTrees: typeof(LookupPageActions));
        var dialogs = context.Render<MudDialogProvider>();
        var cut = context.Render<ReverseTypeAuthLookupPage>();
        cut.WaitForAssertion(() => Assert.NotNull(ActionTreeRows.Find(cut, "Lookup Page Actions")));
        ActionTreeRows.Expand(cut, "Lookup Page Actions", "Warehouses");

        ActionTreeRows.Row(cut, "Warehouses").QuerySelectorAll("button").Single(x => x.TextContent.Trim() == "Search by value").Click();
        dialogs.WaitForElement("[data-testid=reverse-lookup-cancel]");
        Assert.Equal("text", dialogs.Find("input").GetAttribute("type"));

        dialogs.Find("[data-testid=reverse-lookup-cancel]").Click();

        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll("[data-testid=reverse-lookup-search]")));
        Assert.DoesNotContain(transport.Gets, x => x.StartsWith("/ReverseTypeAuthLookup?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_tree_reports_a_text_action_as_needing_a_value()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: "{}", actionTrees: typeof(LookupPageActions));
        ActionPickedEventArgs? picked = null;
        var cut = context.Render<ActionTreeComponent>(p => p.Add(x => x.Tree, "{}").Add(x => x.PickerMode, true)
            .Add(x => x.OnActionPicked, (ActionPickedEventArgs x) => picked = x));
        cut.WaitForAssertion(() => Assert.NotNull(ActionTreeRows.Find(cut, "Lookup Page Actions")));
        ActionTreeRows.Expand(cut, "Lookup Page Actions", "Warehouses");

        ActionTreeRows.Row(cut, "Warehouses").QuerySelectorAll("button").Single(x => x.TextContent.Trim() == "Search by value").Click();

        Assert.NotNull(picked);
        Assert.True(picked!.RequiresValue);
        Assert.False(picked.IsNumber);
        Assert.Equal("*", picked.MaximumValue);
        Assert.Equal("Warehouses", picked.DisplayName);

        // A dynamic number action with no rows is searched one row at a time, so it offers nothing to click.
        var branchDiscount = ActionTreeRows.Row(cut, "Branch Discount");
        Assert.Empty(branchDiscount.QuerySelectorAll("button"));
        Assert.Contains("Searched one row at a time", branchDiscount.TextContent);
    }

    [Fact]
    public async Task Without_the_lookup_action_the_page_shows_no_tree_and_the_menu_no_link()
    {
        await using var context = UserFormHarness.Create(out var transport, accessTree: "{\"ShiftIdentityActions\":{\"Users\":[\"r\"],\"AccessTrees\":[\"r\"]}}", actionTrees: typeof(LookupPageActions));
        var cut = context.Render<ReverseTypeAuthLookupPage>();

        cut.WaitForElement("[data-testid=reverse-lookup-denied]");
        Assert.Null(ActionTreeRows.Find(cut, "Lookup Page Actions"));
        Assert.DoesNotContain(transport.Gets, x => x.StartsWith("/ReverseTypeAuthLookup?", StringComparison.Ordinal));

        Assert.DoesNotContain("ReverseTypeAuthLookup", context.Render<NavMenu>().Markup);
    }

    [Fact]
    public async Task With_the_lookup_action_the_menu_links_to_the_page()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Auditor);

        Assert.Contains("ReverseTypeAuthLookup", context.Render<NavMenu>().Markup);
    }

    private const string Auditor = "{\"ShiftIdentityActions\":{\"ReverseTypeAuthLookup\":[\"m\"]}}";

    [ActionTree("Lookup Page Actions", "")]
    public class LookupPageActions
    {
        public static readonly DecimalAction Discount = new("Discount", null, 0, 100);
        public static readonly TextAction Warehouses = new("Warehouses", TextCombine.UnionList(","), null, maximumAccess: "*");
        public static readonly DynamicDecimalAction BranchDiscount = new("Branch Discount", null, 0, 100);
    }
}
