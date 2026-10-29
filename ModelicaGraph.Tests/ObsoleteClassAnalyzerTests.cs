using ModelicaGraph.Analysis;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>MLQT.Reference.Obsolete</c>, over real source with its dependencies analysed, so the uses are
/// found and located as a check finds them.
/// </summary>
public class ObsoleteClassAnalyzerTests
{
    /// <summary>A library with an obsolete model, an obsolete package, and what replaces them.</summary>
    private const string Library = """
        package Lib
          model Current
            Real x;
          equation
            x = 1;
          end Current;
          model Old "superseded"
            Real x;
          equation
            x = 1;
            annotation (obsolete = "Use Lib.Current instead");
          end Old;
          package Legacy
            constant Real k = 2;
            model Gain
              Real y;
            equation
              y = k;
            end Gain;
            function twice
              input Real u;
              output Real y = 2*u;
            end twice;
            annotation (obsolete = "Legacy will be removed in version 2");
          end Legacy;
          type OldReal = Real(unit = "1") annotation (obsolete = "Use Real");
          model Retired
            Real x;
          equation
            x = 1;
            annotation (obsolete = "Retired" + " for good");
          end Retired;
        end Lib;
        """;

    private static async Task<List<Finding>> Check(string test, StyleCheckingSettings? settings = null)
    {
        var graph = new DirectedGraph();
        var root = Path.Combine(Path.GetTempPath(), "ObsoleteLib");
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(root, "Lib", "package.mo"), Library);
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(root, "Test.mo"), test);
        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        var checkedModels = graph.ModelNodes.Where(m => m.Id == "Test" || m.Id.StartsWith("Test.", StringComparison.Ordinal)).ToList();
        settings ??= new StyleCheckingSettings { CheckObsoleteClasses = true };
        return new ObsoleteClassAnalyzer()
            .Analyze(new GraphAnalysisContext(graph, settings, checkedModels, dependenciesAnalyzed: true))
            .ToList();
    }

    [Fact]
    public async Task AComponentOfAnObsoleteClass_IsReported_WithTheLibrarysMessage()
    {
        var finding = Assert.Single(await Check("""
            model Test
              Lib.Current c;
              Lib.Old o;
            end Test;
            """));

        Assert.Equal(RuleIds.ObsoleteClass, finding.RuleId);
        Assert.Equal("Test", finding.ModelId);
        Assert.Equal("Lib.Old", finding.ElementPath);
        Assert.Equal(3, finding.LineNumber);
        Assert.Equal("Lib.Old is obsolete: Use Lib.Current instead", finding.Message);
    }

    [Fact]
    public async Task EverythingInsideAnObsoletePackage_IsObsolete_WhateverTheUse()
    {
        var findings = await Check("""
            model Test
              Lib.Legacy.Gain g;
              Real y = Lib.Legacy.twice(Lib.Legacy.k);
            end Test;
            """);

        Assert.Equal(["Lib.Legacy.Gain", "Lib.Legacy.twice", "Lib.Legacy"], findings.Select(f => f.ElementPath));
        Assert.Equal("Lib.Legacy.Gain is inside Lib.Legacy, which is obsolete: Legacy will be removed in version 2",
            findings[0].Message);
        Assert.Equal("Lib.Legacy is obsolete: Legacy will be removed in version 2", findings[2].Message);
    }

    [Fact]
    public async Task ExtendingAnObsoleteClass_IsAUse_AndOnlyTheFirstOfEachIsReported()
    {
        var finding = Assert.Single(await Check("""
            model Test
              extends Lib.Old;
              Lib.Old second;
            end Test;
            """));

        Assert.Equal(2, finding.LineNumber);
    }

    [Fact]
    public async Task AnImportedObsoleteClass_IsReportedWhereItIsImported()
    {
        var finding = Assert.Single(await Check("""
            model Test
              import Lib.Old;
              Old o;
            end Test;
            """));

        Assert.Equal("Lib.Old", finding.ElementPath);
        Assert.Equal(2, finding.LineNumber);
    }

    [Fact]
    public async Task AnObsoleteShortClass_IsReported()
    {
        // Its annotation is in its comment rather than a body, and was never read.
        var finding = Assert.Single(await Check("model Test\n  Lib.OldReal r;\nend Test;"));

        Assert.Equal("Lib.OldReal is obsolete: Use Real", finding.Message);
    }

    [Fact]
    public async Task AMarkThatIsNotOneString_StillMarks()
    {
        var finding = Assert.Single(await Check("model Test\n  Lib.Retired r;\nend Test;"));

        Assert.Equal("Lib.Retired is obsolete", finding.Message);
    }

    [Fact]
    public async Task AnObsoleteClass_MayUseOthers()
    {
        // Obsolete classes use each other; the package holding them is not their user.
        Assert.Empty(await Check("""
            model Test
              Lib.Old o;
              annotation (obsolete = "Test is going too");
            end Test;
            """));
        Assert.Empty(await Check("""
            package Test
              model Inner
                Lib.Old o;
              end Inner;
              annotation (obsolete = "all of Test");
            end Test;
            """));
    }

    [Fact]
    public async Task ANestedClass_ReportsItsOwnUses_NotItsPackage()
    {
        var finding = Assert.Single(await Check("""
            package Test
              model Inner
                Lib.Old o;
              end Inner;
            end Test;
            """));

        Assert.Equal("Test.Inner", finding.ModelId);
    }

    [Fact]
    public async Task APackageAndTheClassItHolds_EachReportOnlyTheirOwnUse()
    {
        // The package's own text holds Inner's, so the package's walk meets Inner's use first.
        var findings = await Check("""
            package Test
              model Inner
                Real y = Lib.Legacy.k;
              end Inner;
              constant Real c = Lib.Legacy.k;
            end Test;
            """);

        Assert.Equal([("Test", 5), ("Test.Inner", 2)],
            findings.Select(f => (f.ModelId, f.LineNumber)).OrderBy(p => p.ModelId, StringComparer.Ordinal));
    }

    [Fact]
    public async Task OffByDefault()
    {
        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.ObsoleteClass));
        Assert.Empty(await Check("model Test\n  Lib.Old o;\nend Test;", new StyleCheckingSettings()));
    }

    [Fact]
    public void TheAnnotation_IsReadAtLoad()
    {
        var graph = new DirectedGraph();
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(Path.GetTempPath(), "ObsoleteLib", "Lib", "package.mo"), Library);

        Assert.Equal("Use Lib.Current instead", graph.GetNode<DataTypes.ModelNode>("Lib.Old")!.ObsoleteMessage);
        Assert.Equal(string.Empty, graph.GetNode<DataTypes.ModelNode>("Lib.Retired")!.ObsoleteMessage);
        Assert.Null(graph.GetNode<DataTypes.ModelNode>("Lib.Current")!.ObsoleteMessage);
        Assert.Null(graph.GetNode<DataTypes.ModelNode>("Lib.Legacy.Gain")!.ObsoleteMessage);
    }
}
