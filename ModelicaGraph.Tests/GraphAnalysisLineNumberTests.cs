using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The whole-graph analyses report about a class as a whole, and finding lines are relative to the
/// class (see <see cref="Finding.LineNumber"/>). These analyses knew the class's line in the file
/// and used it, which made their findings the only ones measured from somewhere else — a report
/// mapping them to a file then moved them a second time.
/// </summary>
public class GraphAnalysisLineNumberTests
{
    private static List<Finding> Run(DirectedGraph graph, StyleCheckingSettings settings, IGraphAnalyzer analyzer)
        => GraphAnalysisRunner.Run(
            new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList(), dependenciesAnalyzed: true),
            [analyzer]);

    [Fact]
    public void AnUnusedClass_IsReportedAtItsOwnFirstLine()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("P", "P", "package P\n  model Helper end Helper;\nend P;")
        { ClassType = "package", StartLine = 400 });
        graph.AddNode(new ModelNode("P.Helper", "Helper", "model Helper end Helper;")
        { ClassType = "model", IsNested = true, ParentModelName = "P", IsPublic = false, StartLine = 401 });

        var findings = Run(graph, new StyleCheckingSettings { CheckUnusedClass = true }, new UnusedClassAnalyzer());

        Assert.Equal(1, Assert.Single(findings).LineNumber);
    }

    [Fact]
    public void APackageOrderProblem_IsReportedAtThePackagesFirstLine()
    {
        var graph = new DirectedGraph();
        var package = new ModelNode("P", "P", "package P\n  model A end A;\nend P;")
        { ClassType = "package", StartLine = 400, PackageOrder = ["A", "Ghost"] };
        graph.AddNode(package);
        graph.AddNode(new ModelNode("P.A", "A", "model A end A;")
        { ClassType = "model", IsNested = true, ParentModelName = "P", StartLine = 401 });

        var findings = Run(graph, new StyleCheckingSettings { CheckPackageOrder = true }, new PackageOrderAnalyzer());

        Assert.NotEmpty(findings);
        Assert.All(findings, f => Assert.Equal(1, f.LineNumber));
    }
}
