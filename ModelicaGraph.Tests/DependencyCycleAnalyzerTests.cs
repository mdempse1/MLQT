using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// <c>MLQT.Structure.DependencyCycle</c>: top-level libraries that depend on each other, and none of
/// the cycles inside one library, which are that library's own business.
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

    /// <summary>Libraries named, each a package holding one model <c>M</c>.</summary>
    private void Libraries(params string[] names)
    {
        foreach (var name in names)
        {
            Package(name);
            Model($"{name}.M");
        }
    }

    /// <summary>Lib with Components and Interfaces, each holding one class.</summary>
    private void TwoSubLibraries()
    {
        Package("Lib");
        Package("Lib.Components");
        Package("Lib.Interfaces");
        Model("Lib.Components.Source");
        Model("Lib.Interfaces.Pin");
    }

    [Fact]
    public void LibrariesThatUseEachOther_AreACycle_ReportedOnEachCheckedOne()
    {
        Package("LibA");
        Package("LibA.Blocks");
        Model("LibA.Blocks.Gain");
        Libraries("LibB");
        Uses("LibA.Blocks.Gain", "LibB.M");
        Uses("LibB.M", "LibA.Blocks.Gain");

        var findings = Analyze();

        Assert.Equal(["LibA", "LibB"], findings.Select(f => f.ModelId));
        Assert.All(findings, f => Assert.Equal(RuleIds.DependencyCycle, f.RuleId));
        Assert.All(findings, f => Assert.Equal("LibA, LibB", f.ElementPath));
        Assert.All(findings, f => Assert.Equal(1, f.LineNumber));
        Assert.All(findings, f => Assert.Equal(
            "The libraries LibA and LibB depend on each other: LibA.Blocks.Gain uses LibB.M; LibB.M uses LibA.Blocks.Gain",
            f.Message));
    }

    [Fact]
    public void SubLibrariesThatUseEachOther_AreTheLibrarysOwnBusiness()
    {
        TwoSubLibraries();
        Uses("Lib.Components.Source", "Lib.Interfaces.Pin");
        Uses("Lib.Interfaces.Pin", "Lib.Components.Source");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void ADeepCycleInsideOneLibrary_IsNotReportedEither()
    {
        Package("Lib");
        Package("Lib.Blocks");
        Package("Lib.Blocks.Math");
        Package("Lib.Blocks.Sources");
        Model("Lib.Blocks.Math.Harmonic");
        Model("Lib.Blocks.Sources.Table");
        Uses("Lib.Blocks.Math.Harmonic", "Lib.Blocks.Sources.Table");
        Uses("Lib.Blocks.Sources.Table", "Lib.Blocks.Math.Harmonic");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void SubLibrariesInACycle_DoNotHideOneBetweenLibraries()
    {
        TwoSubLibraries();
        Uses("Lib.Components.Source", "Lib.Interfaces.Pin");
        Uses("Lib.Interfaces.Pin", "Lib.Components.Source");
        Libraries("Other");
        Uses("Lib.Interfaces.Pin", "Other.M");
        Uses("Other.M", "Lib.Components.Source");

        Assert.Equal("Lib, Other", Assert.Single(Analyze("Lib.Interfaces.Pin")).ElementPath);
    }

    [Fact]
    public void ADependencyOneWay_IsNotACycle()
    {
        Libraries("LibA", "LibB");
        Uses("LibA.M", "LibB.M");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void ATopLevelClassThatIsNotAPackage_IsNotALibrary()
    {
        // A lone model file loaded beside a library uses it, and the library names it back.
        Libraries("Lib");
        Model("Helper");
        Uses("Lib.M", "Helper");
        Uses("Helper", "Lib.M");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void ThreeLibraries_AreOneCycle_WithTheShortestRouteRoundIt()
    {
        Libraries("A", "B", "C");
        Uses("A.M", "B.M");
        Uses("B.M", "C.M");
        Uses("C.M", "A.M");

        var findings = Analyze();

        Assert.Equal(["A", "B", "C"], findings.Select(f => f.ModelId));
        Assert.All(findings, f => Assert.Equal("A, B, C", f.ElementPath));
        Assert.All(findings, f => Assert.Equal(
            "The libraries A, B and C depend on each other: A.M uses B.M; B.M uses C.M; C.M uses A.M",
            f.Message));
    }

    [Fact]
    public void TwoSeparateCycles_AreTwoFindingsEach()
    {
        Libraries("A", "B", "C", "D");
        Uses("A.M", "B.M");
        Uses("B.M", "A.M");
        Uses("C.M", "D.M");
        Uses("D.M", "C.M");
        Uses("B.M", "C.M");   // joins them one way only, so they stay two cycles

        Assert.Equal(["A, B", "A, B", "C, D", "C, D"], Analyze().Select(f => f.ElementPath));
    }

    [Fact]
    public void AnExample_IsAnEntryPoint_SoWhatItUsesMakesNoCycle()
    {
        Libraries("LibA", "LibB");
        Model("LibB.Demo", experiment: true);
        Uses("LibA.M", "LibB.M");
        Uses("LibB.Demo", "LibA.M");

        Assert.Empty(Analyze());
    }

    [Fact]
    public void LibrariesThatUseEachOther_GiveTheSameFindings_WhetherCheckedTogetherOrOneAtATime()
    {
        // The MCP server checks one library per call and the desktop app a repository at a time.
        // Reported on the first library only, the one-at-a-time surface never saw the cycle.
        Libraries("LibA", "LibB");
        Uses("LibA.M", "LibB.M");
        Uses("LibB.M", "LibA.M");

        var together = Analyze().Select(f => (f.ModelId, f.ElementPath, f.Message));
        var oneAtATime = Analyze("LibA", "LibA.M").Concat(Analyze("LibB", "LibB.M"))
            .Select(f => (f.ModelId, f.ElementPath, f.Message));

        Assert.Equal(together, oneAtATime);
    }

    [Fact]
    public void ALibraryLoadedButNotChecked_IsFollowed_AndNotReportedOn()
    {
        // A reference library, or an excluded one: its classes are in the graph and are not checked.
        Libraries("LibA", "LibB");
        Uses("LibA.M", "LibB.M");
        Uses("LibB.M", "LibA.M");

        Assert.Equal("LibA", Assert.Single(Analyze("LibA", "LibA.M")).ModelId);
    }

    [Fact]
    public void ACheckOfOneLibrary_ReportsTheCyclesItIsIn_NotEveryCycleLoaded()
    {
        Libraries("A", "B", "C", "D");
        Uses("A.M", "B.M");
        Uses("B.M", "A.M");
        Uses("C.M", "D.M");
        Uses("D.M", "C.M");

        // Found whether or not B is checked, and only A's reported.
        var finding = Assert.Single(Analyze("A.M"));
        Assert.Equal(("A", "A, B"), (finding.ModelId, finding.ElementPath));
        Assert.Equal(4, Analyze().Count);
    }

    [Fact]
    public void TheSameGraph_GivesTheSameFindings_EveryTime()
    {
        // The example named for each step is the first by name, not whichever was met first, so a
        // message - and a baseline built from it - does not move between runs.
        Libraries("LibA", "LibB");
        Model("LibA.Ground");
        Uses("LibA.M", "LibB.M");
        Uses("LibA.Ground", "LibB.M");
        Uses("LibB.M", "LibA.M");

        var first = Analyze("LibA.M").Single();
        var again = Analyze("LibA.M").Single();

        Assert.Equal(first.Message, again.Message);
        Assert.Equal(first.Fingerprint, again.Fingerprint);
        Assert.Contains("LibA.Ground uses LibB.M", first.Message);
    }

    [Fact]
    public void OffByDefault_AndSilentThroughTheRunnerUntilEnabled()
    {
        Libraries("LibA", "LibB");
        Uses("LibA.M", "LibB.M");
        Uses("LibB.M", "LibA.M");
        var checkedClasses = new List<ModelNode> { _graph.GetNode<ModelNode>("LibA.M")! };

        Assert.False(RuleCatalog.IsEnabledByDefault(RuleIds.DependencyCycle));
        Assert.DoesNotContain(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(_graph, new StyleCheckingSettings(), checkedClasses, dependenciesAnalyzed: true)),
            f => f.RuleId == RuleIds.DependencyCycle);

        var finding = Assert.Single(
            GraphAnalysisRunner.Run(new GraphAnalysisContext(_graph, new StyleCheckingSettings { CheckDependencyCycles = true }, checkedClasses, dependenciesAnalyzed: true)),
            f => f.RuleId == RuleIds.DependencyCycle);
        Assert.Equal(RuleSeverity.Warning, finding.Severity);
    }

    [Fact]
    public async Task FromRealSource_TheDependencyAnalysisEdgesGiveTheSameAnswer()
    {
        // The hand-built graphs above stand for what the dependency analysis records; this is that
        // analysis, on two libraries that use each other and a cycle inside one of them.
        var graph = await RealCycleAsync(waived: false);

        var findings = new DependencyCycleAnalyzer().Analyze(new GraphAnalysisContext(
            graph, new StyleCheckingSettings { CheckDependencyCycles = true }, graph.ModelNodes.ToList(), dependenciesAnalyzed: true)).ToList();

        Assert.Equal(["Circuits", "Parts"], findings.Select(f => f.ModelId));
        Assert.All(findings, f => Assert.Equal("Circuits, Parts", f.ElementPath));
        Assert.All(findings, f => Assert.Contains("Parts.Ground uses Circuits.Interfaces.Pin", f.Message));
    }

    [Fact]
    public async Task ACycleIsWaived_OnTheLibraryItIsReportedOn()
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
        var root = Path.Combine(Path.GetTempPath(), "CycleLibs_" + Guid.NewGuid().ToString("N"));
        var waiver = waived ? """  annotation(__MLQT(suppress="MLQT.Structure.DependencyCycle"));""" : "";
        var circuits = Path.Combine(root, "Circuits");
        var parts = Path.Combine(root, "Parts");
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(circuits, "package.mo"), $$"""
            package Circuits
              package Interfaces
                connector Pin
                  Real v;
                  flow Real i;
                end Pin;
                partial model TwoPin
                  Pin p;
                  Pin n;
                  Circuits.Components.Resistor r "A cycle inside the library, not reported";
                end TwoPin;
              end Interfaces;
              package Components
                model Resistor
                  Circuits.Interfaces.Pin p;
                  Parts.Ground g "A part from the other library";
                end Resistor;
              end Components;
            {{waiver}}
            end Circuits;
            """);
        GraphBuilder.LoadModelicaFile(graph, Path.Combine(parts, "package.mo"), $$"""
            package Parts
              model Ground
                Circuits.Interfaces.Pin p;
              equation
                p.v = 0;
              end Ground;
            {{waiver}}
            end Parts;
            """);
        await GraphBuilder.AnalyzeDependenciesAsync(graph, [new LibraryInfo("Circuits", circuits), new LibraryInfo("Parts", parts)]);
        return graph;
    }
}
