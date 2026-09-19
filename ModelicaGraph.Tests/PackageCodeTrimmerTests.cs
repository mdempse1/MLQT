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

    /// <summary>
    /// A package whose standalone children are already stored in their own files has nothing inline
    /// to trim, so it must not be touched at all (B230).
    ///
    /// <para>It used to be rendered anyway — the children were excluded from a tree they were never
    /// in — which left the package holding the renderer's text instead of the file's and
    /// <see cref="ModelNode.SourceMatchesFile"/> false, so every later report fell back to the class
    /// declaration rather than pointing at a line. Measured on a normal load, that was <b>377 of
    /// 688</b> trimmed packages in the Modelica Standard Library and <b>1,172 of 1,235</b> in
    /// Buildings, where 1,073 of them came out longer than the file they came from.</para>
    /// </summary>
    [Fact]
    public void PackageWhoseChildrenAreInTheirOwnFilesIsNotRewritten()
    {
        var graph = new DirectedGraph();
        // Laid out the way a hand-written file is rather than the way the renderer writes one, so
        // that a rewrite shows up in the text and not only in the flag. With a package body the
        // renderer happens to reproduce, this test passes against the unfixed code.
        GraphBuilder.LoadModelicaFile(graph, "P/package.mo", """
            package P "p"
              constant Real    k =  1   "loosely spaced";
            end P;
            """);
        GraphBuilder.LoadModelicaFile(graph, "P/A.mo", """
            within P;
            model A "in its own file"
            end A;
            """);

        var package = graph.GetNode<ModelNode>("P")!;
        var before = package.Definition.ModelicaCode;

        // The premise: P really does have a standalone child, so it is a candidate for trimming and
        // this test is not passing because the trimmer found nothing to consider.
        Assert.True(graph.GetNode<ModelNode>("P.A")!.CanBeStoredStandalone);
        Assert.Equal("P", graph.GetNode<ModelNode>("P.A")!.ParentModelName);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.Equal(before, package.Definition.ModelicaCode);
        Assert.True(package.SourceMatchesFile);
    }

    /// <summary>
    /// The control for the test above: a package with an inline standalone child is still trimmed,
    /// and still says so. Without this, the B230 guard could be widened to "never trim anything" and
    /// nothing here would object.
    /// </summary>
    [Fact]
    public void PackageWithAnInlineChildIsStillTrimmedAndSaysSoOnTheNode()
    {
        var graph = Build();
        var package = graph.GetNode<ModelNode>("P")!;
        Assert.True(package.SourceMatchesFile);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.DoesNotContain("model A", package.Definition.ModelicaCode);
        Assert.False(package.SourceMatchesFile);
    }

    /// <summary>
    /// The usual shape of a real library: a <c>package.mo</c> with some children inline and some in
    /// their own files. <b>One inline child is enough</b> to make the package worth trimming.
    ///
    /// <para>Without this, B230's guard can be inverted from "any child is inline" to "every child
    /// is", and nothing objects — the two agree on a package whose children are all inline and on
    /// one where none are, which is all the other tests here have. A library laid out this way would
    /// then stop being trimmed at all, silently giving back the memory the trimmer exists to save.
    /// Stryker found it as a surviving `Any()` → `All()` mutation.</para>
    /// </summary>
    [Fact]
    public void PackageIsTrimmedWhenOnlySomeOfItsChildrenAreInline()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "R/package.mo", """
            package R "r"
              model Inline "inline in package.mo"
              end Inline;
            end R;
            """);
        GraphBuilder.LoadModelicaFile(graph, "R/Own.mo", """
            within R;
            model Own "in its own file"
            end Own;
            """);

        var package = graph.GetNode<ModelNode>("R")!;
        Assert.Contains("model Inline", package.Definition.ModelicaCode);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.DoesNotContain("model Inline", package.Definition.ModelicaCode);
    }

    /// <summary>
    /// Trimming removes the children and nothing else. The renderer it goes through can be told to
    /// drop annotations, and a package that silently lost its <c>annotation(…)</c> would start
    /// reporting missing icons and missing documentation that are present in the file — findings
    /// about MLQT's own rewrite rather than about the library. Stryker found the option unchecked.
    /// </summary>
    [Fact]
    public void TrimmingKeepsThePackagesOwnAnnotation()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "S/package.mo", """
            package S "s"
              model Child "inline"
              end Child;
              annotation (Documentation(info="<html>kept</html>"));
            end S;
            """);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("S")!.Definition.ModelicaCode;
        Assert.DoesNotContain("model Child", after);
        Assert.Contains("Documentation", after);
        Assert.Contains("kept", after);
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
