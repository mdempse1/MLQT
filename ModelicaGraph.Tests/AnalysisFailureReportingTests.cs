using ModelicaGraph;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// What happens to a class the dependency pass cannot get through.
///
/// <para>The app hangs its style check on this pass rather than walking every class twice, so a
/// class dropped here is a class checked by neither job. Dropping it in silence made a startup run
/// report fewer findings than the same check run again a minute later, with nothing to say which
/// classes had been left out.</para>
/// </summary>
public class AnalysisFailureReportingTests
{
    private static DirectedGraph TwoClasses()
    {
        var graph = new DirectedGraph();
        var file = new FileNode("f1", "P.mo");
        graph.AddNode(file);

        foreach (var name in new[] { "A", "B" })
        {
            var node = new ModelNode($"P.{name}", name, $"model {name}\n  Real x;\nequation\n  x = 1;\nend {name};");
            graph.AddNode(node);
            graph.AddFileContainsModel("f1", node.Id);
        }

        return graph;
    }

    [Fact]
    public async Task AFailingCallback_IsReported()
    {
        var graph = TwoClasses();
        var failures = new List<(string Model, string Error)>();

        await GraphBuilder.AnalyzeDependenciesAsync(
            graph,
            postAnalysisAction: model =>
            {
                if (model.Id == "P.A")
                    throw new InvalidOperationException("checking blew up");
            },
            onModelFailed: (model, ex) => failures.Add((model.Id, ex.Message)));

        Assert.Single(failures);
        Assert.Equal("P.A", failures[0].Model);
        Assert.Contains("blew up", failures[0].Error);
    }

    [Fact]
    public async Task AFailingCallback_DoesNotCostTheModelItsDependencyEdges()
    {
        // The two jobs shared one catch, so a style check that threw also lost the class its edges —
        // and every analysis that reads them then worked from less than it thought.
        var graph = TwoClasses();
        graph.GetNode<ModelNode>("P.A")!.Definition.ModelicaCode =
            "model A\n  B b;\nequation\n  b.x = 1;\nend A;";

        await GraphBuilder.AnalyzeDependenciesAsync(
            graph,
            postAnalysisAction: _ => throw new InvalidOperationException("checking blew up"),
            onModelFailed: (_, _) => { });

        Assert.True(graph.DependenciesAnalyzed);
        Assert.Contains("P.B", graph.GetNode<ModelNode>("P.A")!.UsedModelIds);
    }

    [Fact]
    public async Task AFailingCallback_DoesNotStopTheOtherClasses()
    {
        var graph = TwoClasses();
        var checked_ = new List<string>();

        await GraphBuilder.AnalyzeDependenciesAsync(
            graph,
            postAnalysisAction: model =>
            {
                if (model.Id == "P.A")
                    throw new InvalidOperationException("checking blew up");
                checked_.Add(model.Id);
            },
            onModelFailed: (_, _) => { });

        Assert.Contains("P.B", checked_);
    }

    // ── the targeted pass, which is what a file change runs ──

    [Fact]
    public async Task AFailingCallback_IsReportedByTheTargetedPassToo()
    {
        // Editing a file re-analyses only that file's classes. The same class that would have been
        // dropped in silence on startup must not be dropped in silence here.
        var graph = TwoClasses();
        var failures = new List<(string Model, string Error)>();

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            graph,
            new HashSet<string> { "P.A", "P.B" },
            postAnalysisAction: model =>
            {
                if (model.Id == "P.A")
                    throw new InvalidOperationException("checking blew up");
            },
            onModelFailed: (model, ex) => failures.Add((model.Id, ex.Message)));

        Assert.Equal("P.A", Assert.Single(failures).Model);
    }

    [Fact]
    public async Task TheTargetedPass_KeepsTheEdgesOfAClassWhoseCheckThrew()
    {
        var graph = TwoClasses();
        graph.GetNode<ModelNode>("P.A")!.Definition.ModelicaCode =
            "model A\n  B b;\nequation\n  b.x = 1;\nend A;";

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            graph,
            new HashSet<string> { "P.A" },
            postAnalysisAction: _ => throw new InvalidOperationException("checking blew up"),
            onModelFailed: (_, _) => { });

        Assert.Contains("P.B", graph.GetNode<ModelNode>("P.A")!.UsedModelIds);
    }

    [Fact]
    public async Task TheTargetedPass_TouchesOnlyTheClassesItWasGiven()
    {
        // It runs on every save. Walking the whole graph instead would make an edit to one file cost
        // what a startup costs.
        var graph = TwoClasses();
        var analysed = new List<string>();

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            graph,
            new HashSet<string> { "P.A" },
            postAnalysisAction: model => analysed.Add(model.Id));

        Assert.Equal(["P.A"], analysed);
    }

    [Fact]
    public async Task TheTargetedPass_WithNothingToDo_DoesNothing()
    {
        var graph = TwoClasses();
        var analysed = new List<string>();

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            graph, new HashSet<string>(), postAnalysisAction: model => analysed.Add(model.Id));

        Assert.Empty(analysed);
    }

    [Fact]
    public async Task AClassThatIsNotInTheGraph_IsSkippedRatherThanFatal()
    {
        // The caller's list comes from a file on disk, which can name a class the graph dropped.
        var graph = TwoClasses();
        var analysed = new List<string>();
        var failures = new List<string>();

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(
            graph,
            new HashSet<string> { "P.A", "P.Vanished" },
            postAnalysisAction: model => analysed.Add(model.Id),
            onModelFailed: (model, _) => failures.Add(model.Id));

        Assert.Equal(["P.A"], analysed);
        Assert.Empty(failures);
    }
}
