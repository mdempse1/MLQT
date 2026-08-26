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
}
