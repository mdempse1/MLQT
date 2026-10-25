using MLQT.Shared.Models;
using Xunit;

namespace MLQT.Shared.Tests.Models;

/// <summary>
/// A node's default colours are ones Cytoscape can paint (B512). They were MudBlazor's enum name,
/// "Primary", which is no CSS colour, and only went unnoticed because the one page drawing a graph
/// set both.
/// </summary>
public class DiagramNodeTests
{
    [Fact]
    public void TheDefaultColours_AreThemeVariables_ThatCytoscapeGraphResolves()
    {
        var node = new DiagramNode();

        // cytoscapeGraph.js resolves a value starting "var(" against the document's computed style.
        Assert.StartsWith("var(--mud-palette-", node.Color);
        Assert.StartsWith("var(--mud-palette-", node.BorderColor);
    }
}
