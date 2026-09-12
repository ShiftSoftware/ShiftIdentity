using Bunit;
using ShiftSoftware.ShiftIdentity.Dashboard.Blazor.Pages.User;
using Xunit;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>
/// The action tree inside the Permissions card: every row is a wrapping flex row whose access buttons sit in one
/// right-aligned group, so on a phone the group moves under the action name instead of overflowing the card.
/// </summary>
[Trait("Category", "Ui"), Collection("User form")]
public sealed class ActionTreeLayoutTests
{
    [Fact]
    public async Task Every_action_row_keeps_its_access_buttons_in_one_wrapping_group()
    {
        await using var context = UserFormHarness.Create(out var transport);
        var cut = context.Render<UserForm>();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll("[data-testid=action-tree-row]")));

        var rows = cut.FindAll("[data-testid=action-tree-row]");
        foreach (var row in rows)
        {
            Assert.Contains("action-tree-row", row.ClassName);
            var group = row.QuerySelector("[data-testid=action-tree-access]");
            Assert.NotNull(group);
            Assert.Contains("action-tree-access", group!.ClassName);
            // No button of a row lives outside its group, and no spacer pushes the group off the row.
            Assert.Equal(row.QuerySelectorAll("button").Length, group.QuerySelectorAll("button").Length);
            Assert.Null(row.QuerySelector(".mud-spacer"));
        }
        // The Users action is a read/write/delete action, so its row offers the three access buttons.
        var users = Assert.Single(rows, r => r.TextContent.Contains("Users") && r.QuerySelectorAll("button").Length == 3);
        Assert.Equal(new[] { "Delete", "Write", "Read" }, users.QuerySelectorAll("button").Select(b => b.TextContent.Trim()));

        var style = string.Join(" ", cut.FindAll("style").Select(x => x.TextContent));
        Assert.Contains(".action-tree-row", style); Assert.Contains("flex-wrap: wrap", style);
        Assert.Contains(".action-tree-access", style); Assert.Contains("margin-left: auto", style);
        Assert.Empty(transport.Posts);
    }
}
