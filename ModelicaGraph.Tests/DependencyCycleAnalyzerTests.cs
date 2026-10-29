using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>MLQT.Structure.DependencyCycle</c>: packages under one parent that depend on each other, and
/// none of the class-level cycles that are ordinary Modelica.
/// </summary>
public class DependencyCycleAnalyzerTests
{
    private readonly DirectedGraph _graph = new();

    private ModelNode Package(string id) => Add(id, "package");

    private ModelNode Model(string id, bool experiment = false)
    {
        var node = Add(id, "model");
        node.HasExperimentAnnotation = experiment;
        return node;
    }

    private ModelNode Add(string id, string classType)
    {
        var node = new ModelNode(id, id.Split('.').Last()) { ClassType = classType };
        _graph.AddNode(node);
        return node;
    }

    private void Uses(string user, string used) => _graph.GetNode<ModelNode>(user)!.AddUsedModel(used);

    private List<Finding> Analyze(params string[] checkedClasses)
    {
        var models = checkedClasses.Length == 0
            ? _graph.ModelNodes.ToList()
            : checkedClasses.Select(id => _graph.GetNode<ModelNode>(id)!).ToList();
        return new DependencyCycleAnalyzer()
            .Analyze(new GraphAnalysisContext(_graph, new StyleCheckingSettings { CheckDependencyCycles = true }, models, dependenciesAnalyzed: true))
            .ToList();
    }

    /// <summary>Lib with Components and Interfaces, each holding one class.</summary>
    private void TwoSiblingPackages()
    {
        Package("Lib");
        Package("Lib.Components");
        Package("Lib.Interfaces");
        Model("Lib.Components.Source");
        Model("Lib.Interfaces.Pin");
        Model("Lib.Interfaces.PartialTwoPin");
    }

    [Fact]
    public void TwoPackagesThatUseEachOther_AreOneCycle_OnTheirParent()
    {
        TwoSiblingPackages();
        Uses("Lib.Components.Source", "Lib.Interfaces.PartialTwoPin");
        Uses("Lib.Interfaces.Pin", "Lib.Components.Source");

        var finding = Assert.Single(Analyze());

        Assert.Equal(RuleIds.DependencyCycle, finding.RuleId);
        Assert.Equal("Lib", finding.ModelId);
        Assert.Equal("Lib.Components, Lib.Interfaces", finding.ElementPath);
        Assert.Equal(1, finding.LineNumber);
        Assert.Equal(
            "The packages Components and Interfaces depend on each other: " +
            "Lib.Components.Source uses Lib.Interfaces.PartialTwoPin; Lib.Interfaces.Pin uses Lib.Components.Source",
            finding.Message);
    }

    [Fact]
    public void ADependencyOneWay_IsNotACycle()
    {
        TwoSiblingPackages();
        Uses("Lib.Components.Source", "Lib.Interfaces.Pin");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void ClassesThatReferToEachOther_AreOrdinaryModelica()
    {
        // Inverse functions name each other; a model uses the record it declares a parameter of.
        // Where the names part at two classes, there is no package dependency to report.
        Package("Lib");
        Package("Lib.Functions");
        Model("Lib.Functions.f");
        Model("Lib.Functions.f_inv");
        Uses("Lib.Functions.f", "Lib.Functions.f_inv");
        Uses("Lib.Functions.f_inv", "Lib.Functions.f");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void APackageAndAClassBesideIt_AreNotPackagesDependingOnEachOther()
    {
        Package("Lib");
        Package("Lib.Components");
        Model("Lib.Components.Source");
        Model("Lib.Helper");
        Uses("Lib.Components.Source", "Lib.Helper");
        Uses("Lib.Helper", "Lib.Components.Source");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void UsingItsOwnEnclosingScope_IsNotADependencyBetweenPackages()
    {
        TwoSiblingPackages();
        Uses("Lib.Components.Source", "Lib.Components");   // a constant of its own package
        Uses("Lib.Components.Source", "Lib");
        Uses("Lib.Components", "Lib.Components.Source");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void ADeepCycle_IsReportedWhereTheNamesPart_AndNotAgainAbove()
    {
        // Each dependency counts at one level, so a cycle inside Lib.Blocks is Lib.Blocks' to report,
        // and its classes being deep inside Lib does not make Lib.Blocks depend on anything.
        Package("Lib");
        Package("Lib.Blocks");
        Package("Lib.Blocks.Math");
        Package("Lib.Blocks.Sources");
        Model("Lib.Blocks.Math.Harmonic");
        Model("Lib.Blocks.Sources.Table");
        Uses("Lib.Blocks.Math.Harmonic", "Lib.Blocks.Sources.Table");
        Uses("Lib.Blocks.Sources.Table", "Lib.Blocks.Math.Harmonic");

        var finding = Assert.Single(Analyze());

        Assert.Equal("Lib.Blocks", finding.ModelId);
        Assert.Equal("Lib.Blocks.Math, Lib.Blocks.Sources", finding.ElementPath);
    }

    [Fact]
    public void ThreePackages_AreOneCycle_WithTheShortestRouteRoundIt()
    {
        Package("Lib");
        foreach (var p in new[] { "A", "B", "C" })
        {
            Package($"Lib.{p}");
            Model($"Lib.{p}.M");
        }
        Uses("Lib.A.M", "Lib.B.M");
        Uses("Lib.B.M", "Lib.C.M");
        Uses("Lib.C.M", "Lib.A.M");

        var finding = Assert.Single(Analyze());

        Assert.Equal("Lib.A, Lib.B, Lib.C", finding.ElementPath);
        Assert.Equal(
            "The packages A, B and C depend on each other: Lib.A.M uses Lib.B.M; Lib.B.M uses Lib.C.M; Lib.C.M uses Lib.A.M",
            finding.Message);
    }

    [Fact]
    public void TwoSeparateCycles_UnderOneParent_AreTwoFindings()
    {
        Package("Lib");
        foreach (var p in new[] { "A", "B", "C", "D" })
        {
            Package($"Lib.{p}");
            Model($"Lib.{p}.M");
        }
        Uses("Lib.A.M", "Lib.B.M");
        Uses("Lib.B.M", "Lib.A.M");
        Uses("Lib.C.M", "Lib.D.M");
        Uses("Lib.D.M", "Lib.C.M");
        Uses("Lib.B.M", "Lib.C.M");   // joins them one way only, so they stay two cycles

        Assert.Equal(["Lib.A, Lib.B", "Lib.C, Lib.D"], Analyze().Select(f => f.ElementPath));
    }

    [Fact]
    public void AnExample_IsAnEntryPoint_SoWhatItUsesMakesNoCycle()
    {
        // MSL's Blocks.Examples building circuits from Electrical put most of MSL's top level in one
        // cycle until examples were left out, as MLQT.Unused.Class leaves them out.
        TwoSiblingPackages();
        Model("Lib.Interfaces.Demo", experiment: true);
        Uses("Lib.Components.Source", "Lib.Interfaces.Pin");
        Uses("Lib.Interfaces.Demo", "Lib.Components.Source");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void LibrariesThatUseEachOther_AreACycle_ReportedOnTheFirst()
    {
        Package("LibA");
        Package("LibB");
        Model("LibA.M");
        Model("LibB.M");
        Uses("LibA.M", "LibB.M");
        Uses("LibB.M", "LibA.M");

        var finding = Assert.Single(Analyze());

        Assert.Equal("LibA", finding.ModelId);
        Assert.StartsWith("The libraries LibA and LibB depend on each other", finding.Message);
    }

    [Fact]
    public void ACycleThroughALibraryNotBeingChecked_IsNotThisLibrarysToReport()
    {
        // A reference library, or an excluded one: its classes are in the graph and are not checked.
        Package("LibA");
        Package("LibB");
        Model("LibA.M");
        Model("LibB.M");
        Uses("LibA.M", "LibB.M");
        Uses("LibB.M", "LibA.M");

        Assert.Empty(Analyze("LibA", "LibA.M"));
    }

    [Fact]
    public void TheSameGraph_GivesTheSameFindings_EveryTime()
    {
        // The example named for each step is the first by name, not whichever was met first, so a
        // message - and a baseline built from it - does not move between runs.
        TwoSiblingPackages();
        Model("Lib.Components.Ground");
        Uses("Lib.Components.Source", "Lib.Interfaces.Pin");
        Uses("Lib.Components.Ground", "Lib.Interfaces.Pin");
        Uses("Lib.Interfaces.PartialTwoPin", "Lib.Components.Source");

        var first = Analyze().Single();
        var again = Analyze().Single();

        Assert.Equal(first.Message, again.Message);
        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.Contains("Lib.Components.Ground uses Lib.Interfaces.Pin", first.Message);
    }

    [Fact]
    public void OffByDefault_AndSilentThroughTheRunnerUntilEnabled()
    {
        TwoSiblingPackages();
        Uses("Lib.Components.Source", "Lib.Interfaces.Pin");
        Uses("Lib.Interfaces.Pin", "Lib.Components.Source");

        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.DependencyCycle));
        Assert.DoesNotContain(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(_graph, new StyleCheckingSettings(), _graph.ModelNodes.ToList(), dependenciesAnalyzed: true)),
            f => f.RuleId == RuleIds.DependencyCycle);

        var finding = Assert.Single(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(_graph, new StyleCheckingSettings { CheckDependencyCycles = true }, _graph.ModelNodes.ToList(), dependenciesAnalyzed: true)),
            f => f.RuleId == RuleIds.DependencyCycle);
        Assert.Equal(RuleSeverity.Warning, finding.Severity);
    }

    [Fact]
    public async Task FromRealSource_TheDependencyAnalysisEdgesGiveTheSameAnswer()
    {
        // The hand-built graphs above stand for what the dependency analysis records; this is that
        // analysis, on a library whose interfaces use one of its components.
        var graph = await RealCycleAsync(waived: false);

        var finding = Assert.Single(new DependencyCycleAnalyzer().Analyze(new GraphAnalysisContext(
            graph, new StyleCheckingSettings { CheckDependencyCycles = true }, graph.ModelNodes.ToList(), dependenciesAnalyzed: true)));

        Assert.Equal("Lib", finding.ModelId);
        Assert.Equal("Lib.Components, Lib.Interfaces", finding.ElementPath);
        Assert.Contains("Lib.Interfaces.TwoPin uses Lib.Components.Ground", finding.Message);
    }

    [Fact]
    public async Task ACycleIsWaived_OnThePackageItIsReportedOn()
    {
        // What the settings reference tells a user to do with a cycle they mean to keep.
        var graph = await RealCycleAsync(waived: true);

        Assert.DoesNotContain(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(
                graph, new StyleCheckingSettings { CheckDependencyCycles = true }, graph.ModelNodes.ToList(), dependenciesAnalyzed: true)),
            f => f.RuleId == RuleIds.DependencyCycle);
    }

    private static async Task<DirectedGraph> RealCycleAsync(bool waived)
    {
        var graph = new DirectedGraph();
        var root = Path.Combine(Path.GetTempPath(), "CycleLib_" + Guid.NewGuid().ToString("N"));
        var waiver = waived ? """  annotation(__MLQT(suppress="MLQT.Structure.DependencyCycle"));""" : "";
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(root, "package.mo"), $$"""
            package Lib
              package Interfaces
                connector Pin
                  Real v;
                  flow Real i;
                end Pin;
                partial model TwoPin
                  Pin p;
                  Pin n;
                  Lib.Components.Ground g "A component an interface should not need";
                end TwoPin;
              end Interfaces;
              package Components
                model Ground
                  Lib.Interfaces.Pin p;
                equation
                  p.v = 0;
                end Ground;
              end Components;
            {{waiver}}
            end Lib;
            """);
        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("Lib", root)]);
        return graph;
    }
}
