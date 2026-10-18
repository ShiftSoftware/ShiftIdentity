using Bunit;
using ShiftSoftware.TypeAuth.Core;
using ShiftSoftware.TypeAuth.Core.Actions;
using Xunit;
using ActionTreeComponent = ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Shared.ActionTree.ActionTree;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The action tree's part in exact saves (the server stores a tree only exactly as submitted): it posts the tree as it
/// is, grants beyond the admin included, and shows a text value beyond the admin read-only, since every change to it
/// would be refused.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class ActionTreeSaveTests
{
    // The admin holds Discount 20 and Read on Tickets.
    private const string Admin = """{"SaveTestActions":{"Discount":20,"Tickets":["r"]}}""";

    [Fact]
    public async Task The_tree_is_posted_with_the_grants_beyond_the_admin_left_untouched()
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Admin, actionTrees: typeof(SaveTestActions));
        var cut = Render(context, """{"SaveTestActions":{"Discount":90,"Tickets":["r","w","d"]}}""");

        var posted = new TypeAuthContextBuilder().AddActionTree<SaveTestActions>().AddAccessTree(cut.Instance.GenerateAccessTree()).Build();

        Assert.Equal(90m, posted.AccessValue(SaveTestActions.Discount));
        Assert.True(posted.CanDelete(SaveTestActions.Tickets));
    }

    [Theory]
    [InlineData(90, true)]
    [InlineData(20, false)]
    [InlineData(15, false)]
    public async Task A_text_value_beyond_the_admin_is_read_only(int discount, bool readOnly)
    {
        await using var context = UserFormHarness.Create(out _, accessTree: Admin, actionTrees: typeof(SaveTestActions));
        var cut = Render(context, """{"SaveTestActions":{"Discount":""" + discount + "}}");

        var row = Assert.Single(cut.FindAll("[data-testid=action-tree-row]"), x => x.TextContent.Contains("Discount"));
        Assert.Equal(readOnly, row.QuerySelector("input")!.HasAttribute("readonly"));
    }

    private static IRenderedComponent<ActionTreeComponent> Render(BunitContext context, string tree)
    {
        var cut = context.Render<ActionTreeComponent>(p => p.Add(x => x.Tree, tree));
        cut.WaitForAssertion(() => Assert.Contains(cut.FindAll("[data-testid=action-tree-row]"), x => x.TextContent.Contains("Discount")));
        return cut;
    }

    [ActionTree("Save Test Actions", "")]
    public class SaveTestActions
    {
        public static readonly DecimalAction Discount = new("Discount", null, 0, 100);
        public static readonly ReadWriteDeleteAction Tickets = new("Tickets");
    }
}
