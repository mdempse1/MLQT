using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Driving the library tree the way a user does: expand a package, click a class.
/// </summary>
/// <remarks>
/// <para>Shared because the second copy of it did not work. <c>CodeSearchJourney</c> needs a class
/// selected before there is any code to search, wrote its own two-line version of this, and spent
/// four failing runs on it — the tree is <b>lazy</b>, so clicking a node's label selects it and
/// leaves its children unfetched, and the arrow is what loads them. The version here knows that,
/// knows an already-expanded node has nothing to click, and falls back to a double-click where no
/// arrow is drawn.</para>
///
/// <para>Expanding and selecting are different gestures and this keeps them apart, which is what the
/// screenshots need: a shot of a collapsed tree and a shot of an open one are different pictures.</para>
/// </remarks>
internal static class LibraryTree
{
    /// <summary>
    /// The row a piece of text sits in.
    /// </summary>
    internal static ILocator NodeByText(IPage page, string text) =>
        page.Locator(".mud-treeview-item-content", new PageLocatorOptions { HasTextString = text }).First;

    /// <summary>
    /// Opens <paramref name="nodeText"/>, if it is not open already, and waits for its children.
    /// </summary>
    internal static async Task ExpandAsync(IPage page, string nodeText)
    {
        var node = NodeByText(page, nodeText);
        await node.WaitForAsync(new LocatorWaitForOptions { Timeout = 15_000 });

        var icon = node.Locator(".mud-treeview-item-arrow-expand").First;
        if (await icon.CountAsync() > 0 &&
            (await icon.GetAttributeAsync("class"))?.Contains("mud-transform") == true)
            return;

        var arrow = node.Locator(".mud-treeview-item-arrow button").First;

        if (await arrow.CountAsync() > 0)
            await arrow.ClickAsync();
        else
            await node.DblClickAsync();

        // Server-side children: the node's own click returns before they are fetched.
        await page.WaitForTimeoutAsync(1500);
    }

    /// <summary>
    /// Opens the fixture library's root and selects a class inside it, so the code viewer, the
    /// findings list and the dependency graph have something to show.
    /// </summary>
    internal static async Task SelectClassAsync(IPage page, string className = "Modified", string root = "Lib")
    {
        await ExpandAsync(page, root);

        var target = NodeByText(page, className);
        Assert.True(await target.CountAsync() > 0, $"{className} is not in the tree");

        await target.ClickAsync();
        await page.WaitForTimeoutAsync(1500);
    }
}
