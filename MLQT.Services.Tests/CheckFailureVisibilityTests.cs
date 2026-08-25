using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;
using Xunit;

namespace MLQT.Services.Tests;

/// <summary>
/// A class the checker cannot get through must be reported, not dropped.
///
/// <para>Swallowing the failure cost that class every finding it had. Nothing said so, so the totals
/// moved between runs of the same tool on the same code — and between the app and the CLI — with no
/// way to tell a class that is clean from one that was never checked.</para>
/// </summary>
public class CheckFailureVisibilityTests
{
    private static StyleCheckingSettings Settings() => new() { ClassHasDescription = true };

    private static (DirectedGraph graph, List<ModelNode> models) Library()
    {
        var data = new LibraryDataService();
        data.AddLibraryFromFileAsync("P.mo",
                "package P \"p\"\n  model A\n  end A;\n\n  model B\n  end B;\nend P;")
            .GetAwaiter().GetResult();
        var graph = data.CombinedGraph;
        return (graph, graph.ModelNodes.ToList());
    }

    private static IReadOnlyList<Finding> Check(DirectedGraph graph, IEnumerable<ModelNode> models) =>
        LibraryCheckSession.Check(
            graph, models, Settings(),
            new CustomDictionaryService(), new DictionaryManagerService());

    [Fact]
    public void AClassThatCannotBeChecked_IsReported()
    {
        var (graph, models) = Library();
        var broken = models.First(m => m.Id == "P.A");
        broken.Definition = null!;   // any failure inside the check reaches the same handler

        var findings = Check(graph, models);

        var failure = Assert.Single(findings, f => f.RuleId == RuleIds.CheckFailed);
        Assert.Equal("P.A", failure.ModelId);
        Assert.Equal(RuleSeverity.Error, failure.Severity);
        Assert.Contains("missing from these results", failure.Message);
    }

    [Fact]
    public void AClassThatCannotBeChecked_DoesNotStopTheRest()
    {
        var (graph, models) = Library();
        models.First(m => m.Id == "P.A").Definition = null!;

        var findings = Check(graph, models);

        Assert.Contains(findings, f => f.ModelId == "P.B" && f.RuleId == RuleIds.ClassDescription);
    }

    [Fact]
    public void TheFailureRule_IsInTheCatalog()
    {
        // It reaches the severity map, SARIF metadata and the dashboard like any other rule id.
        Assert.True(RuleCatalog.IsKnown(RuleIds.CheckFailed));
    }
}
