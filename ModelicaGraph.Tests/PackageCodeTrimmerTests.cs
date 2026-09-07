using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The trimmer is a memory optimisation: it drops each package's inline standalone children from its
/// stored source, because they have their own nodes. Checking must not be able to tell — a rule whose
/// result changes with the trim reports different counts on a fresh load and on a file reload, which
/// is exactly what happened to the unused-class rule.
/// </summary>
public class PackageCodeTrimmerTests
{
    private const string PackageSource = """
        package P "p"
          model A "referenced by nothing"
          end A;
          model B "used by C"
          end B;
          model C "uses B"
            B b;
          end C;
        end P;
        """;

    private static DirectedGraph Build()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "P.mo", PackageSource);
        graph.AddModelUsesModel("P.C", "P.B");
        graph.MarkDependenciesAnalyzed();
        return graph;
    }

    private static List<Finding> UnusedFindings(DirectedGraph graph)
    {
        var settings = new StyleCheckingSettings { CheckUnusedPublicClass = true, CheckUnusedClass = true };
        var context = new GraphAnalysisContext(graph, settings, graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(context, new IGraphAnalyzer[] { new UnusedClassAnalyzer() });
    }

    [Fact]
    public void TrimmingDoesNotChangeWhichClassesAreReportedUnused()
    {
        var untrimmed = UnusedFindings(Build());

        var graph = Build();
        PackageCodeTrimmer.TrimStandaloneChildren(graph);
        var trimmed = UnusedFindings(graph);

        Assert.NotEmpty(untrimmed);
        Assert.Equal(
            untrimmed.Select(f => (f.RuleId, f.ModelId)).OrderBy(x => x.ModelId).ToList(),
            trimmed.Select(f => (f.RuleId, f.ModelId)).OrderBy(x => x.ModelId).ToList());
    }

    [Fact]
    public void TrimmingRemovesTheChildrenFromThePackageSource()
    {
        // Guards the premise of the test above: the trim really does change the stored source, so
        // agreement between the two runs is meaningful rather than vacuous.
        var graph = Build();
        var before = graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode;

        PackageCodeTrimmer.TrimStandaloneChildren(graph);
        var after = graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode;

        Assert.Contains("model A", before);
        Assert.DoesNotContain("model A", after);
    }

    [Fact]
    public void RepeatedTrimIsANoOp()
    {
        var graph = Build();
        PackageCodeTrimmer.TrimStandaloneChildren(graph);
        var afterFirst = graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode;

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.True(graph.GetNode<ModelNode>("P")!.ChildrenTrimmed);
        Assert.Equal(afterFirst, graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode);
    }

    [Fact]
    public void ReloadedPackageIsTrimmedAgain()
    {
        // A reload drops the file's models and re-parses it, so the package comes back with the full
        // source read from disk (LibraryDataService.ReloadFileAsync / UpdateChangedFilesAsync do
        // exactly this). The GUI's Refresh button, VCS operations and saving an edit all go through
        // that path, and each used to leave the library in the untrimmed state that startup and the
        // CLI never check in.
        var graph = Build();
        PackageCodeTrimmer.TrimStandaloneChildren(graph);
        Assert.DoesNotContain("model A", graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode);

        foreach (var id in graph.ModelNodes.Select(m => m.Id).ToList())
            graph.RemoveNode(id);
        GraphBuilder.LoadModelicaFile(graph, "P.mo", PackageSource);

        Assert.False(graph.GetNode<ModelNode>("P")!.ChildrenTrimmed);
        Assert.Contains("model A", graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.DoesNotContain("model A", graph.GetNode<ModelNode>("P")!.Definition.ModelicaCode);
    }

    [Fact]
    public void VisibilityIsLoadedOntoTheNodes_NotDerivedFromTheTrimmedSource()
    {
        const string source = """
            package Q "q"
              model Pub "public"
              end Pub;
            protected
              model Prot "protected"
              end Prot;
            end Q;
            """;
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "Q.mo", source);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.True(graph.GetNode<ModelNode>("Q.Pub")!.IsPublic);
        Assert.False(graph.GetNode<ModelNode>("Q.Prot")!.IsPublic);
    }
}
