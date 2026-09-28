using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Which classes the second loadSelector pass reads (B282).
/// </summary>
/// <remarks>
/// <para>The pass finds <c>T comp(file = "...")</c>, where <c>T</c> declares <c>file</c> as a
/// loadSelector or loadResource parameter. A class can only do that by declaring a component of such a
/// <c>T</c>, which is a use of <c>T</c> — so the pass is asked of the classes that use one, not of the
/// whole graph. It parsed every class a second time before: 68,746 on Claytex with the Dymola library
/// folder, to find two resources that 1,211 users would have been enough to find.</para>
///
/// <para>The same rule fixed the incremental path, which decided whether to run the pass from the
/// classes being re-analysed: unless one of them itself declared a tracked parameter it skipped the
/// pass, so a class edited to set another class's file parameter lost that resource until the next
/// full analysis.</para>
/// </remarks>
public class LoadSelectorPassTwoScopeTests
{
    private static readonly string DataFile = Path.Combine(Path.GetTempPath(), "mlqt-b282", "specific.mat");

    private const string TableCode = """
        model Table
          parameter String tableFile = "NoName"
            annotation (Dialog(loadSelector(filter="MATLAB MAT-files (*.mat)", caption="Open file")));
        end Table;
        """;

    private static string UserCode(string file) => $$"""
        model User
          Table table(tableFile = "{{file.Replace("\\", "/")}}");
        end User;
        """;

    private static bool HasSelectorEdge(DirectedGraph graph) =>
        graph.ResourceEdges.Any(e =>
            e.ModelId == "User"
            && e.ReferenceType == ResourceReferenceType.LoadSelector
            && e.ParameterName == "tableFile");

    [Fact]
    public async Task AUserOfAClassWithATrackedParameter_HasItsModificationFound()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "table.mo", TableCode);
        GraphBuilder.LoadModelicaFile(graph, "user.mo", UserCode(DataFile));

        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        Assert.True(HasSelectorEdge(graph));
    }

    [Fact]
    public async Task ReanalysingOnlyTheUser_StillFindsIt()
    {
        // The incremental path, as after an edit to user.mo alone. The class being re-analysed
        // declares no tracked parameter of its own; the class it modifies does.
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "table.mo", TableCode);
        var userIds = GraphBuilder.LoadModelicaFile(graph, "user.mo", UserCode(DataFile));
        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        // Drop what the full analysis found, then re-analyse the user alone.
        graph.RemoveModelResourceEdges("User");
        Assert.False(HasSelectorEdge(graph));

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string>(userIds));

        Assert.True(HasSelectorEdge(graph));
    }

    [Fact]
    public void OnlyAClassUsingACarrier_IsAsked()
    {
        var graph = new DirectedGraph();
        var table = new ModelNode("Table", "Table", "model Table end Table;");
        table.LoadSelectorParameters.Add("tableFile");
        var user = new ModelNode("User", "User", "model User end User;");
        var bystander = new ModelNode("Bystander", "Bystander", "model Bystander end Bystander;");
        graph.AddNode(table);
        graph.AddNode(user);
        graph.AddNode(bystander);
        graph.AddModelUsesModel("User", "Table");

        Assert.True(GraphBuilder.MayModifyATrackedParameter(graph, user));
        Assert.False(GraphBuilder.MayModifyATrackedParameter(graph, bystander));
        Assert.False(GraphBuilder.MayModifyATrackedParameter(graph, table));   // declaring is not modifying
    }
}
