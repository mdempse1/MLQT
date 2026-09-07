using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// An import is visible to every class lexically nested below the one that declares it, and a package
/// directory's children are lexically nested inside its package.mo — so the uses are usually in other
/// files. Checking the declaring class alone reported the top package of essentially every real
/// library (MSL's Modelica.Blocks included) as full of unused imports.
/// </summary>
public class UnusedImportAnalyzerTests
{
    private static ModelNode Package(string id, string code)
        => new(id, id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id, code) { ClassType = "package" };

    private static ModelNode Model(string id, string code)
        => new(id, id.Contains('.') ? id[(id.LastIndexOf('.') + 1)..] : id, code) { ClassType = "model" };

    private static List<Finding> Run(params ModelNode[] models) => Run(models, models);

    /// <param name="reported">The models under check; the rest are still in the graph (so they can
    /// account for an import) but nothing is reported against them.</param>
    private static List<Finding> Run(IEnumerable<ModelNode> all, IEnumerable<ModelNode> reported)
    {
        var graph = new DirectedGraph();
        foreach (var m in all) graph.AddNode(m);
        var settings = new StyleCheckingSettings { CheckUnusedImports = true };
        var ctx = new GraphAnalysisContext(graph, settings, reported.ToList());
        return GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedImportAnalyzer() });
    }

    private static string? UnusedAlias(List<Finding> findings, string modelId) => findings
        .FirstOrDefault(f => f.RuleId == RuleIds.UnusedImport && f.ModelId == modelId)?.ElementPath;

    [Fact]
    public void ImportUsedOnlyByAClassInAnotherFile_IsNotFlagged()
    {
        // The reported case: Claytex/package.mo declares the imports, the classes that use them are
        // separate .mo files under Claytex/.
        var root = Package("Claytex", "package Claytex \"lib\"\n  import SI = Modelica.Units.SI;\nend Claytex;");
        var child = Model("Claytex.Shaft", "model Shaft \"s\"\n  SI.Angle phi;\nend Shaft;");

        Assert.DoesNotContain(Run(root, child), f => f.RuleId == RuleIds.UnusedImport);
    }

    [Fact]
    public void ImportUsedByADeeplyNestedClass_IsNotFlagged()
    {
        var root = Package("Lib", "package Lib \"lib\"\n  import SI = Modelica.Units.SI;\nend Lib;");
        var mid = Package("Lib.Parts", "package Parts \"p\"\nend Parts;");
        var leaf = Model("Lib.Parts.Deep.Thing", "model Thing \"t\"\n  SI.Mass m;\nend Thing;");

        Assert.DoesNotContain(Run(root, mid, leaf), f => f.RuleId == RuleIds.UnusedImport);
    }

    [Fact]
    public void ImportNothingUses_IsStillFlagged()
    {
        var root = Package("Lib", "package Lib \"lib\"\n  import SI = Modelica.Units.SI;\nend Lib;");
        var child = Model("Lib.Thing", "model Thing \"t\"\n  Real x;\nend Thing;");

        Assert.Equal("SI", UnusedAlias(Run(root, child), "Lib"));
    }

    [Fact]
    public void ImportUsedInTheDeclaringClassItself_IsNotFlagged()
    {
        var m = Model("M", "model M \"m\"\n  import SI = Modelica.Units.SI;\n  SI.Length x;\nend M;");
        Assert.DoesNotContain(Run(m), f => f.RuleId == RuleIds.UnusedImport);
    }

    [Fact]
    public void UseInASiblingClass_DoesNotCount()
    {
        // A sibling is not nested inside the declaring class, so it cannot see the import.
        var a = Model("Lib.A", "model A \"a\"\n  import SI = Modelica.Units.SI;\n  Real x;\nend A;");
        var b = Model("Lib.B", "model B \"b\"\n  SI.Length y;\nend B;");

        Assert.Equal("SI", UnusedAlias(Run(a, b), "Lib.A"));
    }

    [Fact]
    public void UseInAnotherLibraryWithASimilarName_DoesNotCount()
    {
        // "LibExtra" starts with the same characters as "Lib" but is not nested inside it — the scope
        // test has to be on the dotted path, not on the string prefix.
        var lib = Package("Lib", "package Lib \"lib\"\n  import SI = Modelica.Units.SI;\nend Lib;");
        var other = Model("LibExtra.Thing", "model Thing \"t\"\n  SI.Length x;\nend Thing;");

        Assert.Equal("SI", UnusedAlias(Run(lib, other), "Lib"));
    }

    [Fact]
    public void UseInAStandaloneNestedClass_CountsThroughItsOwnNode()
    {
        // A standalone nested class is checked as its own node and is trimmed out of its parent's
        // stored source, so the parent's parse tree never sees the use — the graph walk has to.
        var outer = Model("M", "model M \"m\"\n  import Modelica.Blocks.Continuous;\nend M;");
        var inner = Model("M.Inner", "model Inner \"i\"\n  Continuous.Integrator i;\nend Inner;");

        Assert.DoesNotContain(Run(outer, inner), f => f.RuleId == RuleIds.UnusedImport);
    }

    [Fact]
    public void WildcardImport_IsNeverFlagged()
    {
        var m = Model("M", "model M \"m\"\n  import Modelica.Units.SI.*;\n  Real x;\nend M;");
        Assert.DoesNotContain(Run(m), f => f.RuleId == RuleIds.UnusedImport);
    }

    [Fact]
    public void ADescendantOutsideTheCheckedSet_StillAccountsForTheImport()
    {
        // Checking one package must not report its imports as unused just because the classes using
        // them were not part of this run (the GUI checks a repository at a time).
        var root = Package("Lib", "package Lib \"lib\"\n  import SI = Modelica.Units.SI;\nend Lib;");
        var child = Model("Lib.Thing", "model Thing \"t\"\n  SI.Mass m;\nend Thing;");

        Assert.DoesNotContain(
            Run([root, child], [root]),
            f => f.RuleId == RuleIds.UnusedImport);
    }

    [Fact]
    public void NothingIsReportedForAModelOutsideTheCheckedSet()
    {
        var a = Model("Lib.A", "model A \"a\"\n  import SI = Modelica.Units.SI;\n  Real x;\nend A;");
        var b = Model("Lib.B", "model B \"b\"\n  import Cv = Modelica.Units.Conversions;\n  Real y;\nend B;");

        var findings = Run([a, b], [a]);

        Assert.Equal("SI", UnusedAlias(findings, "Lib.A"));
        Assert.DoesNotContain(findings, f => f.ModelId == "Lib.B");
    }

    [Fact]
    public void SeveralImports_AreReportedIndividually()
    {
        var m = Model("M",
            "model M \"m\"\n  import SI = Modelica.Units.SI;\n  import Cv = Modelica.Units.Conversions;\n" +
            "  SI.Length x;\nend M;");

        var f = Assert.Single(Run(m), x => x.RuleId == RuleIds.UnusedImport);
        Assert.Equal("Cv", f.ElementPath);
        Assert.Equal(3, f.LineNumber);
    }

    [Fact]
    public void TheRuleIsSkippedWhenItIsOff()
    {
        var graph = new DirectedGraph();
        var m = Model("M", "model M \"m\"\n  import SI = Modelica.Units.SI;\n  Real x;\nend M;");
        graph.AddNode(m);
        var ctx = new GraphAnalysisContext(graph, new StyleCheckingSettings(), [m]);

        Assert.Empty(GraphAnalysisRunner.Run(ctx, new IGraphAnalyzer[] { new UnusedImportAnalyzer() }));
    }
}
