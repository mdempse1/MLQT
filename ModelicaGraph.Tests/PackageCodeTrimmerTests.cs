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
///
/// <para><b>The surviving mutants, read rather than chased (B216).</b> The item asked for the file's
/// seven survivors to be finished with the rewrite, and the honest end state is that each is killed
/// by a test, gone with the code, or judged here. Three went with the render: the parse-error guard,
/// the short-class early return and <c>excludeClassDefinitions: false</c> are not in the excising
/// version at all. Four are killed by the tests below — the standalone rule, the duplicate-name
/// rule, the per-child file check and CRLF handling. What is left is judged equivalent and recorded
/// so the next audit does not re-raise it:</para>
///
/// <list type="bullet">
/// <item>The package-level <c>Any(...)</c> filter is now a pure optimisation. The per-child loop
/// asks the same question again before cutting anything, so widening the filter lets more packages
/// into the loop and they come out with no ranges and unchanged.</item>
/// <item><c>if (standaloneNames.Count == 0) return;</c> is the same shape: with no names, no ranges
/// are built and the method returns on the next check anyway.</item>
/// <item><c>lowerName != "package"</c> guards a class literally named <c>package</c>, which cannot
/// exist — it is a reserved word, so such a file does not parse and no node is ever created. A test
/// for it was written, found to assert nothing, and removed.</item>
/// <item>The range guards (<c>first &lt; 2</c>, <c>last &gt;= lines.Length</c>) and
/// <c>OwnsItsLines</c>'s leading-whitespace check are bounds checks on data a loader does not
/// produce: a child of a package is inside it. They turn a swallowed <c>IndexOutOfRangeException</c>
/// into a clean skip, and a test would have to fabricate a node to reach them.</item>
/// </list>
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
    ///
    /// <para><b>What it says changed with B216.</b> Trimming used to re-render the package, so its
    /// lines were the renderer's and <c>SourceMatchesFile</c> went false. It now excises the
    /// children's lines, so what is left is the file's own text and the node carries the
    /// <see cref="ModelNode.TrimElision"/> that says which lines are missing — the mapping is kept
    /// rather than abandoned.</para>
    /// </summary>
    [Fact]
    public void PackageWithAnInlineChildIsStillTrimmedAndSaysSoOnTheNode()
    {
        var graph = Build();
        var package = graph.GetNode<ModelNode>("P")!;
        Assert.True(package.SourceMatchesFile);
        Assert.Null(package.TrimElision);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        Assert.DoesNotContain("model A", package.Definition.ModelicaCode);
        Assert.True(package.SourceMatchesFile);
        Assert.NotNull(package.TrimElision);
        Assert.False(package.TrimElision!.IsEmpty);
    }

    /// <summary>
    /// The point of excising rather than re-rendering: every line that survives is the file's own,
    /// and the elision says where it came from.
    /// </summary>
    [Fact]
    public void WhatIsLeftIsTheFilesOwnText_AndTheElisionMapsItBack()
    {
        var graph = Build();
        var package = graph.GetNode<ModelNode>("P")!;
        var original = package.Definition.ModelicaCode.Replace("\r\n", "\n").Split('\n');

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var trimmed = package.Definition.ModelicaCode.Split('\n');
        var elision = package.TrimElision!;

        // Every remaining line is character-for-character the line the elision says it came from.
        for (var display = 1; display <= trimmed.Length; display++)
            Assert.Equal(original[elision.ToSourceLine(display) - 1], trimmed[display - 1]);
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
    /// A child that cannot be stored standalone stays in the package's source, because that is
    /// where it has to live: <c>replaceable</c>, <c>redeclare</c>, <c>inner</c> and <c>outer</c>
    /// classes cannot be pulled out into a file of their own.
    /// </summary>
    [Fact]
    public void ANonStandaloneChildIsNotExcised()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "W.mo", """
            package W "w"
              replaceable model Inner "cannot be standalone"
              end Inner;
              model Plain "can be"
              end Plain;
            end W;
            """);

        Assert.False(graph.GetNode<ModelNode>("W.Inner")!.CanBeStoredStandalone);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("W")!.Definition.ModelicaCode;
        Assert.Contains("model Inner", after);
        Assert.DoesNotContain("model Plain", after);
    }

    /// <summary>
    /// Two children whose names differ only in case cannot both be written to their own file on a
    /// case-insensitive filesystem, so neither is treated as standalone — the same rule
    /// <c>ModelicaPackageSaver</c> applies when it writes them out.
    /// </summary>
    [Fact]
    public void ChildrenWhoseNamesDifferOnlyInCaseAreNotExcised()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "X.mo", """
            package X "x"
              model Thing "one"
              end Thing;
              model thing "the other"
              end thing;
            end X;
            """);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("X")!.Definition.ModelicaCode;
        Assert.Contains("model Thing", after);
        Assert.Contains("model thing", after);
    }

    /// <summary>
    /// The stored source can have CRLF endings — it is a slice of a file, and every file in the
    /// Modelica Standard Library and in Buildings is CRLF. Splitting on the wrong terminator would
    /// make the line numbers meaningless and the excision cut in the wrong places.
    /// </summary>
    [Fact]
    public void ACrlfStoredSourceIsTrimmedCorrectly()
    {
        var graph = new DirectedGraph();
        var crlf = string.Join(new string([(char)13, (char)10]),
            "package Z \"z\"", "  model A \"a\"", "  end A;", "  constant Real k = 1;", "end Z;", "");
        GraphBuilder.LoadModelicaFile(graph, "Z.mo", crlf);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("Z")!.Definition.ModelicaCode;
        Assert.DoesNotContain("model A", after);
        Assert.Contains("constant Real k = 1;", after);
        Assert.Contains("end Z;", after);
    }

    /// <summary>
    /// A child that does not have its lines to itself is left where it is (B216).
    ///
    /// <para>Excising works by dropping whole lines, so a class sharing a line with its neighbour
    /// cannot go without taking that neighbour with it. It is kept instead, which costs nothing: a
    /// rule visitor skips a nested standalone class definition because it has its own node and is
    /// checked there. <c>ElisionFinder</c> makes the same call for the same reason.</para>
    /// </summary>
    [Fact]
    public void AChildSharingALineWithItsNeighbourIsNotExcised()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "T.mo", """
            package T "t"
              model A "a" end A; model B "b" end B;
            end T;
            """);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("T")!.Definition.ModelicaCode;
        Assert.Contains("model A", after);
        Assert.Contains("model B", after);
    }

    /// <summary>
    /// The control for the test above, on the same shape: one child per line really is excised, so
    /// the guard is about sharing a line and not about this fixture.
    /// </summary>
    [Fact]
    public void AChildOnItsOwnLinesIsExcised()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "U.mo", """
            package U "u"
              model A "a"
              end A;
              model B "b"
              end B;
            end U;
            """);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("U")!.Definition.ModelicaCode;
        Assert.DoesNotContain("model A", after);
        Assert.DoesNotContain("model B", after);
        Assert.Contains("package U", after);
        Assert.Contains("end U;", after);
    }

    /// <summary>
    /// A sibling class beside the package in the same file is not one of its children to cut out.
    /// Its lines are outside the package's, and subtracting the package's start line from them
    /// gives a range that means nothing in the package's own text.
    /// </summary>
    [Fact]
    public void ASiblingInTheSameFileIsNotTouched()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, "V.mo", """
            package V "v"
              model A "a"
              end A;
            end V;

            model Beside "not part of V"
            end Beside;
            """);

        PackageCodeTrimmer.TrimStandaloneChildren(graph);

        var after = graph.GetNode<ModelNode>("V")!.Definition.ModelicaCode;
        Assert.DoesNotContain("model A", after);
        Assert.DoesNotContain("Beside", after);   // never was in V's source
        Assert.Contains("end V;", after);
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
