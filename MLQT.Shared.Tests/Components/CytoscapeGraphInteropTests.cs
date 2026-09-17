using Bunit;
using MLQT.Shared.Components;
using MLQT.Shared.Models;
using Xunit;

namespace MLQT.Shared.Tests.Components;

/// <summary>
/// <see cref="CytoscapeGraph"/> is a wrapper whose entire behaviour is six JS calls, so what is
/// worth asserting is the call sequence and its payload — not what Cytoscape draws, which is an
/// engine question and belongs to the <c>/selftest</c> route in 7a-7.
///
/// <para>The reason to pin the calls at all: the Photino migration changes the webview engine, and
/// these calls are the seam. A test that says "init is sent once, with these elements, and destroy
/// follows on disposal" is the thing that stays true across the host swap.</para>
/// </summary>
public class CytoscapeGraphInteropTests : MlqtComponentTestBase
{
    private static readonly DiagramNode[] TwoNodes =
    [
        new() { Id = "A", Label = "A", FullName = "Lib.A" },
        new() { Id = "B", Label = "B", FullName = "Lib.B" },
    ];

    private static readonly DiagramEdge[] OneEdge = [new() { FromId = "A", ToId = "B" }];

    [Fact]
    public void ARenderedGraph_SendsInitOnceWithItsElements()
    {

        Render<CytoscapeGraph>(p => p
            .Add(c => c.Nodes, TwoNodes)
            .Add(c => c.Edges, OneEdge)
            .Add(c => c.Layout, "dagre"));

        var call = Assert.Single(JSInterop.Invocations["cytoscapeGraph.init"]);

        // container id, elements, the DotNetObjectReference for callbacks, layout name
        Assert.Equal(4, call.Arguments.Count);
        Assert.Equal("dagre", call.Arguments[3]);

        var elements = Assert.IsType<object[]>(call.Arguments[1]);
        Assert.Equal(3, elements.Length); // two nodes and the edge between them
    }

    [Fact]
    public void AGraphWithNoNodes_SendsNothing()
    {
        // Initialising an empty container would leave Cytoscape holding a graph it can never be
        // told about, because the update path only runs once init has happened.

        Render<CytoscapeGraph>(p => p
            .Add(c => c.Nodes, Array.Empty<DiagramNode>())
            .Add(c => c.Edges, Array.Empty<DiagramEdge>()));

        Assert.Empty(JSInterop.Invocations["cytoscapeGraph.init"]);
    }

    [Fact]
    public void NewData_SendsUpdateRatherThanASecondInit()
    {

        var cut = Render<CytoscapeGraph>(p => p
            .Add(c => c.Nodes, TwoNodes)
            .Add(c => c.Edges, OneEdge));

        cut.Render(p => p
            .Add(c => c.Nodes, new DiagramNode[] { new() { Id = "C", Label = "C", FullName = "Lib.C" } })
            .Add(c => c.Edges, Array.Empty<DiagramEdge>()));

        Assert.Single(JSInterop.Invocations["cytoscapeGraph.init"]);
        Assert.Single(JSInterop.Invocations["cytoscapeGraph.update"]);
    }

    [Fact]
    public void ReRenderingWithTheSameData_SendsNothingFurther()
    {
        // The guard against a redraw on every parent StateHasChanged: the component compares
        // references, so an unchanged collection must not reach the engine again.

        var cut = Render<CytoscapeGraph>(p => p
            .Add(c => c.Nodes, TwoNodes)
            .Add(c => c.Edges, OneEdge));

        cut.Render(p => p
            .Add(c => c.Nodes, TwoNodes)
            .Add(c => c.Edges, OneEdge));

        Assert.Empty(JSInterop.Invocations["cytoscapeGraph.update"]);
    }

    [Fact]
    public async Task RelayoutAndHighlight_ReachTheEngineWithTheSameContainer()
    {

        var cut = Render<CytoscapeGraph>(p => p
            .Add(c => c.Nodes, TwoNodes)
            .Add(c => c.Edges, OneEdge));

        var container = Assert.Single(JSInterop.Invocations["cytoscapeGraph.init"]).Arguments[0];

        await cut.InvokeAsync(() => cut.Instance.RelayoutAsync("klay"));
        await cut.InvokeAsync(() => cut.Instance.HighlightNodeAsync("A"));
        await cut.InvokeAsync(() => cut.Instance.ClearHighlightAsync());

        Assert.Equal(new[] { container, "klay" }, Assert.Single(JSInterop.Invocations["cytoscapeGraph.relayout"]).Arguments);
        Assert.Equal(new[] { container, "A" }, Assert.Single(JSInterop.Invocations["cytoscapeGraph.highlight"]).Arguments);
        Assert.Equal(new[] { container }, Assert.Single(JSInterop.Invocations["cytoscapeGraph.clearHighlight"]).Arguments);
    }

    [Fact]
    public async Task DisposingAnInitialisedGraph_TellsTheEngineToDestroyIt()
    {
        // Without this the engine keeps the instance and its listeners for a container that is gone,
        // which is a leak the user meets as a tab that gets slower every time they open it.

        var cut = Render<CytoscapeGraph>(p => p
            .Add(c => c.Nodes, TwoNodes)
            .Add(c => c.Edges, OneEdge));

        await cut.Instance.DisposeAsync();

        Assert.Single(JSInterop.Invocations["cytoscapeGraph.destroy"]);
    }

    [Fact]
    public async Task DisposingAGraphThatNeverInitialised_TellsTheEngineNothing()
    {

        var cut = Render<CytoscapeGraph>(p => p.Add(c => c.Nodes, Array.Empty<DiagramNode>()));

        await cut.Instance.DisposeAsync();

        Assert.Empty(JSInterop.Invocations["cytoscapeGraph.destroy"]);
    }
}
