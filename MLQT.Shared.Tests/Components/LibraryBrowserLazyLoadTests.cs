using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MLQT.Services.Interfaces;
using MLQT.Shared.Components;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Moq;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// The one component behaviour in this suite that cannot exist without a render tree, and the
/// regression that made the case for a GUI harness in the first place.
///
/// <para><b>What broke.</b> <c>MudTreeView</c> loads a node's children through <c>ServerData</c>.
/// From MudBlazor 9.4 the control stopped keeping those children against the item it loaded them
/// for, so selecting anything below the first level silently did nothing — no
/// <c>SelectedValueChanged</c>, no model change, no error. The fix is the <c>ItemsChanged</c>
/// binding on <c>MudTreeViewItem</c>, which writes the loaded children back onto the
/// <c>TreeItemData</c> (<see cref="LibraryBrowser"/>'s <c>OnNodeChildrenLoaded</c>).</para>
///
/// <para><b>Why it needs bUnit.</b> Every piece in isolation is fine: <c>ServerData</c> returns the
/// children, the write-back assigns them, the selection handler calls <c>ChangeModelID</c>. The
/// defect is in how MudBlazor threads them together, so only a real render tree can show it. This
/// is the whole justification for bUnit being in the plan at all — everything else in
/// <c>MLQT.Shared.Tests</c> calls a method directly.</para>
/// </summary>
public class LibraryBrowserLazyLoadTests : MlqtComponentTestBase
{
    private static ModelNode Package(string id, string name) =>
        new(id, name, $"package {name} end {name};") { ClassType = "package" };

    private static ModelNode Model(string id, string name) =>
        new(id, name, $"model {name} end {name};") { ClassType = "model" };

    private readonly ModelNode _root = Package("Lib", "Lib");
    private readonly ModelNode _child = Package("Lib.Sub", "Sub");
    private readonly ModelNode _grandchild = Model("Lib.Sub.Leaf", "Leaf");

    private Mock<ILibraryDataService> ArrangeThreeLevelLibrary()
    {
        var library = new Mock<ILibraryDataService>();
        library.SetupGet(l => l.CombinedGraph).Returns(new DirectedGraph());
        library.Setup(l => l.GetTopLevelModelsAsync())
               .ReturnsAsync(new List<ModelNode> { _root });

        // The lazy-load path: MudTreeView asks for a node's children only when it is expanded.
        library.Setup(l => l.GetChildModelsAsync(It.Is<ModelNode>(n => n != null && n.Id == _root.Id)))
               .ReturnsAsync(new List<ModelNode> { _child });
        library.Setup(l => l.GetChildModelsAsync(It.Is<ModelNode>(n => n != null && n.Id == _child.Id)))
               .ReturnsAsync(new List<ModelNode> { _grandchild });
        library.Setup(l => l.GetChildModelsAsync(It.Is<ModelNode>(n => n == null || n.Id == _grandchild.Id)))
               .ReturnsAsync(new List<ModelNode>());

        library.Setup(l => l.ModelHasChildren(It.IsAny<string>()))
               .Returns<string>(id => id != _grandchild.Id);

        Services.AddSingleton(library.Object);
        Services.AddSingleton(new Mock<IRepositoryService>().Object);
        Services.AddSingleton(new Mock<IFileMonitoringService>().Object);
        return library;
    }

    private IRenderedComponent<LibraryBrowser> RenderBrowser()
    {
        RenderProviders();
        return Render<LibraryBrowser>(p => p.Add(c => c.LibraryOnly, true));
    }

    [Fact]
    public void TheTreeRendersItsTopLevelLibraries()
    {
        ArrangeThreeLevelLibrary();

        var browser = RenderBrowser();

        Assert.Contains("Lib", browser.Markup);
    }

    [Fact]
    public async Task ExpandingANode_LoadsItsChildrenThroughServerData()
    {
        var library = ArrangeThreeLevelLibrary();
        var browser = RenderBrowser();

        await ExpandFirstCollapsedNodeAsync(browser);

        browser.WaitForAssertion(() => Assert.Contains("Sub", browser.Markup));
        library.Verify(l => l.GetChildModelsAsync(It.Is<ModelNode>(n => n != null && n.Id == _root.Id)),
                       Times.AtLeastOnce);
    }

    [Fact]
    public async Task SelectingALazilyLoadedChild_ChangesTheSelectedModel()
    {
        // The regression, stated as an assertion: a node that arrived through ServerData must be
        // selectable. Before the ItemsChanged write-back this silently did nothing.
        ArrangeThreeLevelLibrary();
        var browser = RenderBrowser();

        await ExpandFirstCollapsedNodeAsync(browser);
        browser.WaitForAssertion(() => Assert.Contains("Sub", browser.Markup));

        await ClickNodeContainingAsync(browser, "Sub");

        browser.WaitForAssertion(() => Assert.Equal(_child.Id, NavState.ModelID));
    }

    [Fact]
    public async Task SelectingAGrandchild_ChangesTheSelectedModel()
    {
        // One level deeper than the reported regression, because the write-back has to hold for
        // every level and not merely the first that anyone happened to try.
        ArrangeThreeLevelLibrary();
        var browser = RenderBrowser();

        await ExpandFirstCollapsedNodeAsync(browser);
        browser.WaitForAssertion(() => Assert.Contains("Sub", browser.Markup));

        await ExpandFirstCollapsedNodeAsync(browser);
        browser.WaitForAssertion(() => Assert.Contains("Leaf", browser.Markup));

        await ClickNodeContainingAsync(browser, "Leaf");

        browser.WaitForAssertion(() => Assert.Equal(_grandchild.Id, NavState.ModelID));
    }

    /// <summary>
    /// Clicks the expand arrow of the first node that is collapsed and expandable.
    /// </summary>
    /// <remarks>
    /// The element is found <b>inside</b> <c>InvokeAsync</c>, and so is every other find-then-click
    /// in this project. An element found on the test thread carries the event-handler ids of the
    /// render it was taken from; a lazily loaded tree re-renders on its own when the server data
    /// arrives, so a find outside the dispatcher can be clicked after the handler it names has gone.
    /// bUnit reports that as <c>UnknownEventHandlerIdException</c>, and it reported it exactly once,
    /// on the Linux runner, against a different test of the same shape (B156).
    /// </remarks>
    private static Task ExpandFirstCollapsedNodeAsync(IRenderedComponent<LibraryBrowser> browser) =>
        browser.InvokeAsync(() =>
        {
            var arrow = browser.FindAll(".mud-treeview-item-arrow-expand")
                               .FirstOrDefault(e => !e.ClassList.Contains("mud-transform"));
            Assert.NotNull(arrow);
            arrow!.Click();
        });

    /// <summary>Clicks the content of the tree item whose text contains <paramref name="text"/>.</summary>
    private static Task ClickNodeContainingAsync(IRenderedComponent<LibraryBrowser> browser, string text) =>
        browser.InvokeAsync(() =>
        {
            var node = browser.FindAll(".mud-treeview-item-content")
                              .FirstOrDefault(e => e.TextContent.Contains(text));
            Assert.NotNull(node);
            node!.Click();
        });
}
