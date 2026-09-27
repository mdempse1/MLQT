using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Tests;

/// <summary>
/// B458 — a file whose <c>within</c> clause does not name the package whose directory it is stored
/// in. MLS 3.6 section 13.4.3: "For a sub-entity of an enclosing structured entity, the within-clause
/// shall designate the class of the enclosing entity".
///
/// <para>Nothing here touches the disk: the analyzer reads the paths the graph was loaded with, so
/// each file is loaded under a path in a directory that never exists.</para>
/// </summary>
public class WithinClauseAnalyzerTests
{
    /// <summary>A root that is never created, so these paths mean the same thing on every platform.</summary>
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "mlqt-within-" + Guid.NewGuid().ToString("N"));

    private static string At(params string[] segments) => Path.Combine([Root, .. segments]);

    private static DirectedGraph Load(params (string Path, string Code)[] files)
    {
        var graph = new DirectedGraph();
        foreach (var (path, code) in files)
            GraphBuilder.LoadModelicaFile(graph, path, code);
        return graph;
    }

    private static List<Finding> Analyze(DirectedGraph graph, StyleCheckingSettings? settings = null)
    {
        var ctx = new GraphAnalysisContext(graph, settings ?? new StyleCheckingSettings(), graph.ModelNodes.ToList());
        return GraphAnalysisRunner.Run(ctx).Where(f => f.RuleId == RuleIds.WithinClause).ToList();
    }

    private static readonly (string, string) LibPackage =
        (At("Lib", "package.mo"), "within;\npackage Lib\n  package Q\n  end Q;\nend Lib;\n");

    private static readonly (string, string) SubPackage =
        (At("Lib", "Sub", "package.mo"), "within Lib;\npackage Sub\nend Sub;\n");

    // ---- the mismatches ------------------------------------------------------------------------

    [Fact]
    public void AFileInTheLibraryDirectory_SayingWithinANestedPackage_IsReported()
    {
        // B449's layout: R loads as Lib.Q.R, which no other tool would look for in Lib/.
        var graph = Load(LibPackage, (At("Lib", "R.mo"), "within Lib.Q;\nmodel R\nend R;\n"));

        var finding = Assert.Single(Analyze(graph));
        Assert.Equal("Lib.Q.R", finding.ModelId);
        Assert.Equal(RuleSeverity.Error, finding.Severity);
        Assert.Contains("within Lib.Q;", finding.Message);
        Assert.Contains("must say 'within Lib;'", finding.Message);
    }

    [Fact]
    public void AFileInASubPackageDirectory_SayingWithinTheLibrary_IsReported()
    {
        var graph = Load(LibPackage, SubPackage, (At("Lib", "Sub", "X.mo"), "within Lib;\nmodel X\nend X;\n"));

        var finding = Assert.Single(Analyze(graph));
        Assert.Equal("Lib.X", finding.ModelId);
        Assert.Contains("must say 'within Lib.Sub;'", finding.Message);
    }

    [Fact]
    public void AFileBelowTheTopLevel_WithNoWithinClause_IsReported()
    {
        var graph = Load(LibPackage, SubPackage, (At("Lib", "Sub", "X.mo"), "model X\nend X;\n"));

        var finding = Assert.Single(Analyze(graph));
        Assert.Equal("X", finding.ModelId);
        Assert.Contains("has no within clause", finding.Message);
    }

    [Fact]
    public void AnEmptyWithinClauseBelowTheTopLevel_IsReported()
    {
        // `within;` is how a top-level class says it is one; below the top level it is as wrong as
        // saying nothing.
        var graph = Load(LibPackage, (At("Lib", "R.mo"), "within;\nmodel R\nend R;\n"));

        Assert.Single(Analyze(graph));
    }

    [Fact]
    public void ASubPackagesPackageMo_NamingTheWrongParent_IsReportedOnce()
    {
        // The package.mo is the one wrong file. X below it names Lib.Sub correctly and is not a second
        // finding, because the expected name is built from the class names, not from what Sub's own
        // clause made of it.
        var graph = Load(
            LibPackage,
            (At("Lib", "Sub", "package.mo"), "within Lib.Q;\npackage Sub\nend Sub;\n"),
            (At("Lib", "Sub", "X.mo"), "within Lib.Sub;\nmodel X\nend X;\n"));

        var finding = Assert.Single(Analyze(graph));
        Assert.Equal("Lib.Q.Sub", finding.ModelId);
        Assert.Contains("must say 'within Lib;'", finding.Message);
    }

    // ---- the controls --------------------------------------------------------------------------

    [Fact]
    public void ALibraryWhoseFilesAllSayWhereTheyAre_IsAccepted()
    {
        var graph = Load(
            LibPackage,
            SubPackage,
            (At("Lib", "R.mo"), "within Lib;\nmodel R\nend R;\n"),
            (At("Lib", "Sub", "X.mo"), "within Lib.Sub;\nmodel X\nend X;\n"),
            (At("Lib", "Sub", "Deeper", "package.mo"), "within Lib.Sub;\npackage Deeper\nend Deeper;\n"),
            (At("Lib", "Sub", "Deeper", "Y.mo"), "within Lib.Sub.Deeper;\nmodel Y\nend Y;\n"));

        Assert.Empty(Analyze(graph));
    }

    [Fact]
    public void TheRootPackage_IsNotJudged_WhateverItsWithinSays()
    {
        // A repository holding one sub-package of a larger library: its root quite properly says
        // `within Modelica;`, and the files below it are judged against that loaded name.
        var graph = Load(
            (At("Blocks", "package.mo"), "within Modelica;\npackage Blocks\nend Blocks;\n"),
            (At("Blocks", "Gain.mo"), "within Modelica.Blocks;\nblock Gain\nend Gain;\n"));

        Assert.Empty(Analyze(graph));
    }

    [Fact]
    public void AFileInADirectoryThatIsNotAPackage_IsNotJudged()
    {
        // A top-level entity: a loose .mo file, or a single-file library at a repository's root.
        var graph = Load((At("Loose", "Lib.mo"), "within Somewhere;\npackage Lib\nend Lib;\n"));

        Assert.Empty(Analyze(graph));
    }

    [Fact]
    public void NestedClassesAreNotJudged_OnlyTheClassHeadingTheFile()
    {
        var graph = Load(
            (At("Lib", "package.mo"), "within;\npackage Lib\n  package Inner\n    model M\n    end M;\n  end Inner;\nend Lib;\n"),
            (At("Lib", "R.mo"), "within Lib;\npackage R\n  model S\n  end S;\nend R;\n"));

        Assert.Empty(Analyze(graph));
    }

    [Fact]
    public void AFileWithASyntaxError_IsStillJudged_ByItsWithinClause()
    {
        // The clause is read before the class body, so a recovered parse still says where the class
        // was put, and the misplacement is as real as the syntax error.
        var graph = Load(LibPackage, (At("Lib", "R.mo"), "within Lib.Q;\nmodel R\n  this is not Modelica\n"));

        Assert.Single(Analyze(graph));
    }

    [Fact]
    public void ADirectoryWhosePackageMoYieldedNoClass_JudgesNothingBelowIt()
    {
        // No class came out of package.mo, so there is no package name to judge against; the parse
        // failure is the finding.
        var graph = Load(
            (At("Lib", "package.mo"), "%%% not Modelica at all %%%\n"),
            (At("Lib", "R.mo"), "within Elsewhere;\nmodel R\nend R;\n"));

        Assert.Empty(Analyze(graph));
    }

    // ---- configuration -------------------------------------------------------------------------

    [Fact]
    public void ItIsOnByDefault_AndCanBeSwitchedOff()
    {
        var graph = Load(LibPackage, (At("Lib", "R.mo"), "within Lib.Q;\nmodel R\nend R;\n"));
        var off = new StyleCheckingSettings();
        off.SetRuleEnabled(RuleIds.WithinClause, false);

        Assert.Single(Analyze(graph));
        Assert.Empty(Analyze(graph, off));
    }

    [Fact]
    public void ItCanBeSuppressedOnTheClass()
    {
        var graph = Load(LibPackage, (At("Lib", "R.mo"),
            "within Lib.Q;\nmodel R\n  annotation(__MLQT(suppress=\"MLQT.Structure.WithinClause\"));\nend R;\n"));

        Assert.Empty(Analyze(graph));
    }

    [Fact]
    public void OnlyTheCheckedClassesAreReported()
    {
        var graph = Load(LibPackage, (At("Lib", "R.mo"), "within Lib.Q;\nmodel R\nend R;\n"));
        var ctx = new GraphAnalysisContext(graph, new StyleCheckingSettings(),
            graph.ModelNodes.Where(m => m.Id != "Lib.Q.R").ToList());

        Assert.DoesNotContain(GraphAnalysisRunner.Run(ctx), f => f.RuleId == RuleIds.WithinClause);
    }
}
