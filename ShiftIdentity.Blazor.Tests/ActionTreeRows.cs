using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;

namespace ShiftIdentity.Blazor.Tests;

/// <summary>Finds and opens rows of the action tree, which renders a folder's rows only while the folder is expanded.</summary>
internal static class ActionTreeRows
{
    public static IReadOnlyList<IElement> All<T>(IRenderedComponent<T> cut) where T : IComponent =>
        cut.FindAll("[data-testid=action-tree-row]");

    public static IElement? Find<T>(IRenderedComponent<T> cut, string label) where T : IComponent =>
        All(cut).FirstOrDefault(r => r.QuerySelector(".action-tree-label")!.TextContent.Trim() == label);

    public static IElement Row<T>(IRenderedComponent<T> cut, string label) where T : IComponent =>
        Find(cut, label) ?? throw new InvalidOperationException($"No action tree row '{label}'.");

    /// <summary>Clicks the folder's row, as a user does, and waits until the row <paramref name="child"/> shows.</summary>
    public static void Expand<T>(IRenderedComponent<T> cut, string label, string child) where T : IComponent
    {
        Click(cut, label);
        cut.WaitForAssertion(() => Xunit.Assert.NotNull(Find(cut, child)));
    }

    /// <summary>Clicks the folder's row and waits until the row <paramref name="child"/> is gone.</summary>
    public static void Collapse<T>(IRenderedComponent<T> cut, string label, string child) where T : IComponent
    {
        Click(cut, label);
        cut.WaitForAssertion(() => Xunit.Assert.Null(Find(cut, child)));
    }

    // The tree expands or collapses a folder on a click on its row; the change renders after the click returns.
    private static void Click<T>(IRenderedComponent<T> cut, string label) where T : IComponent =>
        Row(cut, label).Closest(".mud-treeview-item-content")!.Click();
}
