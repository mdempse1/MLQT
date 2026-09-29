using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <see cref="ReloadedClassChanges"/>: which classes a reload of whole libraries changed, for the VCS
/// pipeline, which cannot otherwise tell (B499). The reload is played here by replacing each node with
/// a new one, as removing and loading a library again does.
/// </summary>
public class ReloadedClassChangesTests
{
    private static readonly string[] Ids = ["P", "P.Child", "P.Sub", "P.Sub.Deep"];

    private static DirectedGraph Graph(string package)
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", package));
        graph.AddNode(new ModelNode("P.Child", "Child", "model Child\nend Child;"));
        graph.AddNode(new ModelNode("P.Sub", "Sub", "package Sub\nend Sub;"));
        graph.AddNode(new ModelNode("P.Sub.Deep", "Deep", "model Deep\nend Deep;"));
        return graph;
    }

    /// <summary>Every class loaded again, as new nodes, <c>P</c> with <paramref name="package"/>.</summary>
    private static DirectedGraph Reloaded(string package) => Graph(package);

    [Fact]
    public void AnImportAddedToAPackageItsClassesAsked_ReportsEveryClassBelowIt()
    {
        var before = Graph("package P\nend P;");
        ClassImports.For(before.GetNode<ModelNode>("P")!.Definition);   // dependency analysis asked
        var changes = ReloadedClassChanges.Capture(before, Ids);

        var after = Reloaded("package P\n  import SI = Modelica.Units.SI;\nend P;");

        Assert.Equal(["P", "P.Child", "P.Sub", "P.Sub.Deep"], changes.ClassesToReanalyse(after, Ids));
    }

    [Fact]
    public void AWaiverOnlyTheNestedPackageCarries_ReportsOnlyWhatItEncloses()
    {
        var before = Graph("package P\nend P;");
        ClassSuppressions.For(before.GetNode<ModelNode>("P.Sub")!.Definition, "P.Sub");
        var changes = ReloadedClassChanges.Capture(before, Ids);

        var after = Graph("package P\nend P;");
        after.GetNode<ModelNode>("P.Sub")!.Definition.ModelicaCode =
            "package Sub\n  annotation(__MLQT(suppress=\"*\"));\nend Sub;";

        Assert.Equal(["P.Sub", "P.Sub.Deep"], changes.ClassesToReanalyse(after, Ids));
    }

    [Fact]
    public void WaiversAndImportsNobodyRead_WidenNothing()
    {
        // Nothing was checked or resolved with them, so nothing below can be stale. Reading them here
        // would parse every package of every reloaded library to find that out.
        var changes = ReloadedClassChanges.Capture(Graph("package P\nend P;"), Ids);

        var after = Reloaded("package P\n  import SI = Modelica.Units.SI;\n  annotation(__MLQT(suppress=\"*\"));\nend P;");

        Assert.Equal(["P"], changes.ClassesToReanalyse(after, Ids));
    }

    [Fact]
    public void AClassTheReloadAdded_IsReported_AndOneItRemovedIsNot()
    {
        var before = Graph("package P\nend P;");
        var changes = ReloadedClassChanges.Capture(before, ["P", "P.Child", "P.Sub", "P.Sub.Deep", "P.Gone"]);

        var after = Graph("package P\nend P;");
        after.AddNode(new ModelNode("P.New", "New", "model New\nend New;"));

        Assert.Equal(["P.New"], changes.ClassesToReanalyse(after, [.. Ids, "P.New", "P.Gone"]));
    }

    [Fact]
    public void AnIdTheGraphDoesNotHold_IsNotCaptured()
    {
        var changes = ReloadedClassChanges.Capture(Graph("package P\nend P;"), ["P", "Q"]);

        var after = Graph("package P\nend P;");
        after.AddNode(new ModelNode("Q", "Q", "package Q\nend Q;"));

        // Q was never recorded, so it is new to this snapshot.
        Assert.Equal(["Q"], changes.ClassesToReanalyse(after, ["P", "Q"]));
    }
}
