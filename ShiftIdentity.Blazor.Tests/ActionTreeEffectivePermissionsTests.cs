using Bunit;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;
using ActionTreeComponent = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree.ActionTree;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// Effective Permissions shows a user's trees together, read-only, as TypeAuth decides with them. They aren't merged
/// into one tree first: one tree can't hold a wildcard on a dynamic action and rows beyond it, and the merged tree showed
/// TCA's users without the self row's Delete that "Data by Branch: Self" gives them next to "System: Basic".
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class ActionTreeEffectivePermissionsTests
{
    // A wildcard on Regions, and the self row of Regions beyond it.
    private const string Wildcard = """{"EffectiveTestActions":{"Regions":["r","w"]}}""";
    private const string SelfRow = """{"EffectiveTestActions":{"Regions":{"_shift_software_type_auth_core_self_reference":["r","w","d"]}}}""";

    static ActionTreeEffectivePermissionsTests() => EffectiveTestActions.Regions.Expand([new("7", "North")], addSelf: true);

    [Fact]
    public async Task A_row_beyond_another_trees_wildcard_shows_what_the_user_can_do()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: "{}", actionTrees: typeof(EffectiveTestActions));
        var cut = Render(context, Wildcard, SelfRow);
        ActionTreeRows.Expand(cut, "Effective Test Actions", "Regions");
        ActionTreeRows.Expand(cut, "Regions", "Self");

        Assert.True(Ticked(cut, "Self", "Delete"));
        Assert.True(Ticked(cut, "Self", "Write"));
        Assert.True(Ticked(cut, "North", "Write"));
        Assert.False(Ticked(cut, "North", "Delete"));
    }

    // A wildcard without Maximum gives a text action its minimum; another tree's value goes beyond it.
    [Fact]
    public async Task A_value_beyond_another_trees_wildcard_shows_the_value_the_user_holds()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: "{}", actionTrees: typeof(EffectiveTestActions));
        var cut = Render(context, """{"EffectiveTestActions":["r"]}""", """{"EffectiveTestActions":{"Discount":40}}""");
        ActionTreeRows.Expand(cut, "Effective Test Actions", "Discount");

        Assert.Equal("40", ActionTreeRows.Row(cut, "Discount").QuerySelector("input")!.GetAttribute("value"));
    }

    [Fact]
    public async Task Several_trees_are_shown_only_read_only()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: "{}", actionTrees: typeof(EffectiveTestActions));

        Assert.Throws<InvalidOperationException>(() => context.Render<ActionTreeComponent>(p => p.Add(x => x.Trees, [Wildcard, SelfRow])));
    }

    private static IRenderedComponent<ActionTreeComponent> Render(BunitContext context, params string[] trees)
    {
        var cut = context.Render<ActionTreeComponent>(p => p.Add(x => x.Trees, trees).Add(x => x.ReadOnly, true).Add(x => x.Disabled, true));
        cut.WaitForAssertion(() => Assert.NotNull(ActionTreeRows.Find(cut, "Effective Test Actions")));
        return cut;
    }

    // An access button shows checked as a filled button.
    private static bool Ticked(IRenderedComponent<ActionTreeComponent> cut, string row, string label) =>
        ActionTreeRows.Row(cut, row).QuerySelectorAll("button").Single(x => x.TextContent.Trim() == label).ClassList.Contains("mud-button-filled");

    [ActionTree("Effective Test Actions", "")]
    public class EffectiveTestActions
    {
        public static readonly DynamicReadWriteDeleteAction Regions = new("Regions");
        public static readonly DecimalAction Discount = new("Discount", null, 0, 100);
    }
}
