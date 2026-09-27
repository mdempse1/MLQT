using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// B445 — a file's text outside every class is read once at load, onto the class that heads the
/// file, and kept out of every class's stored source.
/// </summary>
public class FileTextLoadTests
{
    private static DirectedGraph Load(string content, string path = "Lib/P.mo")
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, path, content);
        return graph;
    }

    [Fact]
    public void TheHeaderIsCarriedByTheClassThatHeadsTheFile_AndByNoOther()
    {
        var graph = Load("// Copyright header\r\nwithin Lib;\r\npackage P\r\n  model Inner\r\n  end Inner;\r\nend P;\r\n");

        var head = graph.GetNode<ModelNode>("Lib.P")!;
        Assert.Equal("// Copyright header\n", head.FileText!.Leading);
        Assert.Null(graph.GetNode<ModelNode>("Lib.P.Inner")!.FileText);
    }

    [Fact]
    public void TheHeaderIsNotPartOfAnyStoredSource()
    {
        var graph = Load("// Copyright header\nwithin Lib;\npackage P\n  model Inner\n  end Inner;\nend P;\n");

        Assert.All(graph.ModelNodes, m => Assert.DoesNotContain("Copyright", m.Definition.ModelicaCode));
    }

    [Fact]
    public void AFileWithNothingOutsideItsClassCarriesNothing()
        => Assert.Null(Load("within Lib;\nmodel M\nend M;\n").GetNode<ModelNode>("Lib.M")!.FileText);

    [Fact]
    public void CommentsAfterTheClauseAndAfterTheClassAreCarriedToo()
    {
        // Both positions parse since B430, and neither is in the class's stored source, so this is
        // the only place a writer can find them.
        var graph = Load("within Lib; // after\nmodel M\nend M; // trailer\n");

        var text = graph.GetNode<ModelNode>("Lib.M")!.FileText!;
        Assert.Contains("// after", text.AfterWithin);
        Assert.Contains("// trailer", text.Trailing);
    }
}
