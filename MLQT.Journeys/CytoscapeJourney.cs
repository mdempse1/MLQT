using Microsoft.Playwright;
using Xunit;

namespace MLQT.Journeys;

/// <summary>
/// Journey 6 — the dependency graph actually draws.
/// </summary>
/// <remarks>
/// <para>The most engine-sensitive thing MLQT does, and the reason it is worth a journey rather than
/// a unit test: <c>CytoscapeGraphInteropTests</c> already pins that the right calls are made with
/// the right payloads, and that is all a headless renderer can say. Whether Cytoscape then
/// <em>instantiates</em>, resolves its layout extensions and reports a graph is a question about the
/// browser, and it is exactly the question phase 7b asks about WebKitGTK.</para>
///
/// <para>Asserted by reading the Cytoscape instance rather than by looking at pixels. Screenshot
/// diffing across two engines produces false positives on every glyph; "the graph has three nodes
/// and two edges" is true or it is not.</para>
/// </remarks>
[Collection(JourneyCollection.Name)]
public class CytoscapeJourney(TestHostFixture host)
{
    /// <summary>Two nodes and the edge between them, in the shape CytoscapeGraph.razor sends.</summary>
    private const string Elements = """
        [
          { group: 'nodes', data: { id: 'A', label: 'A', color: '#6a70b1', borderColor: '#333', fullName: 'Lib.A' } },
          { group: 'nodes', data: { id: 'B', label: 'B', color: '#6a70b1', borderColor: '#333', fullName: 'Lib.B' } },
          { group: 'edges', data: { id: 'edge-A-B', source: 'A', target: 'B' } }
        ]
        """;

    private async Task<IPage> PageWithContainerAsync()
    {
        var page = await host.NewPageAsync();
        await page.GotoAsync(host.BaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });

        // A container of a real size: Cytoscape measures its element, and a zero-height div produces
        // a graph that reports nodes and renders nothing - which is the failure this journey is
        // meant to distinguish from a broken engine.
        await page.EvaluateAsync("""
            () => {
              const d = document.createElement('div');
              d.id = 'journey-graph';
              d.style.width = '600px';
              d.style.height = '400px';
              document.body.appendChild(d);
            }
            """);
        return page;
    }

    [Fact]
    public async Task AGraphInitialises_AndReportsWhatItWasGiven()
    {
        var page = await PageWithContainerAsync();

        await page.EvaluateAsync($"() => window.cytoscapeGraph.init('journey-graph', {Elements}, null, 'dagre-tb')");

        var nodes = await page.EvaluateAsync<int>(
            "() => window._cytoscapeInstances['journey-graph'].cy.nodes().length");
        var edges = await page.EvaluateAsync<int>(
            "() => window._cytoscapeInstances['journey-graph'].cy.edges().length");

        Assert.Equal(2, nodes);
        Assert.Equal(1, edges);
    }

    [Fact]
    public async Task TheGraphIsLaidOut_NotStackedAtTheOrigin()
    {
        // A layout extension that failed to register does not throw: Cytoscape falls back and leaves
        // every node at the same position, which draws as one blob. Distinct positions are what says
        // dagre actually ran.
        var page = await PageWithContainerAsync();

        await page.EvaluateAsync($"() => window.cytoscapeGraph.init('journey-graph', {Elements}, null, 'dagre-tb')");
        await page.WaitForTimeoutAsync(500);

        var distinct = await page.EvaluateAsync<int>("""
            () => {
              const cy = window._cytoscapeInstances['journey-graph'].cy;
              const seen = new Set();
              cy.nodes().forEach(n => seen.add(`${Math.round(n.position('x'))},${Math.round(n.position('y'))}`));
              return seen.size;
            }
            """);

        Assert.Equal(2, distinct);
    }

    [Theory]
    [InlineData("dagre-tb")]
    [InlineData("dagre-lr")]
    [InlineData("breadthfirst")]
    [InlineData("klay")]
    [InlineData("fcose")]
    public async Task EveryLayoutTheUiOffers_Runs(string layout)
    {
        // Each of these is a separate extension script, and each registers itself against the global
        // Cytoscape defines. One that failed to load is invisible until a user picks that layout.
        var page = await PageWithContainerAsync();
        var errors = new List<string>();
        page.PageError += (_, e) => errors.Add(e);

        await page.EvaluateAsync($"() => window.cytoscapeGraph.init('journey-graph', {Elements}, null, '{layout}')");
        await page.WaitForTimeoutAsync(500);

        Assert.Empty(errors);
        Assert.Equal(2, await page.EvaluateAsync<int>(
            "() => window._cytoscapeInstances['journey-graph'].cy.nodes().length"));
    }

    [Fact]
    public async Task ChangingTheLayout_KeepsTheGraph()
    {
        var page = await PageWithContainerAsync();
        await page.EvaluateAsync($"() => window.cytoscapeGraph.init('journey-graph', {Elements}, null, 'dagre-tb')");

        await page.EvaluateAsync("() => window.cytoscapeGraph.relayout('journey-graph', 'breadthfirst')");
        await page.WaitForTimeoutAsync(500);

        Assert.Equal(2, await page.EvaluateAsync<int>(
            "() => window._cytoscapeInstances['journey-graph'].cy.nodes().length"));
    }

    [Fact]
    public async Task DestroyingAGraph_LetsItGo()
    {
        // The leak a user meets as a tab that gets slower every time they open it.
        var page = await PageWithContainerAsync();
        await page.EvaluateAsync($"() => window.cytoscapeGraph.init('journey-graph', {Elements}, null, 'dagre-tb')");

        await page.EvaluateAsync("() => window.cytoscapeGraph.destroy('journey-graph')");

        var stillThere = await page.EvaluateAsync<bool>(
            "() => window._cytoscapeInstances['journey-graph'] !== undefined");
        Assert.False(stillThere);
    }
}
