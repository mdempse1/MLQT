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

    /// <summary>
    /// The finding that says "these results are incomplete" must not be something a baseline can
    /// accept. It was, for as long as <c>IsParseDiagnostic</c> was the predicate the baseline asked:
    /// one `baseline create` over a library with a class MLQT threw on wrote the failure in as debt,
    /// and the gate never mentioned it again.
    /// </summary>
    [Fact]
    public void AFailureIsNeverWrittenToABaseline()
    {
        var (graph, models) = Library();
        models.First(m => m.Id == "P.A").Definition = null!;
        var findings = Check(graph, models);
        Assert.Contains(findings, f => f.RuleId == RuleIds.CheckFailed);

        var baseline = Baseline.FromFindings(findings);

        Assert.DoesNotContain(baseline.Entries, e => e.RuleId == RuleIds.CheckFailed);
        Assert.All(findings.Where(f => f.RuleId == RuleIds.CheckFailed),
            f => Assert.False(baseline.Contains(f)));
    }

    /// <summary>
    /// And not even when the baseline was written before this rule existed and happens to hold the
    /// fingerprint anyway — <c>Contains</c> refuses on the rule id, not on the absence of an entry.
    /// </summary>
    [Fact]
    public void AFailureIsNotAcceptedEvenByABaselineThatRecordsIt()
    {
        var failure = new Finding
        {
            RuleId = RuleIds.CheckFailed,
            ModelId = "P.A",
            Message = "Checking this class failed.",
            Severity = RuleSeverity.Error,
        };
        var stale = new Baseline([
            new BaselineEntry(failure.Fingerprint, failure.RuleId, failure.ModelId, null, failure.Message)
        ]);

        Assert.False(stale.Contains(failure));
    }

    [Fact]
    public void EveryDiagnosticIsRecognisedAsOne()
    {
        // The whole point of the predicate: three ids, one answer. Adding a fourth diagnostic without
        // adding it here is what let CheckFailed be treated as style debt in the first place.
        Assert.True(RuleIds.IsDiagnostic(RuleIds.SyntaxError));
        Assert.True(RuleIds.IsDiagnostic(RuleIds.ParseFailure));
        Assert.True(RuleIds.IsDiagnostic(RuleIds.CheckFailed));
        Assert.False(RuleIds.IsDiagnostic(RuleIds.ClassDescription));

        // Narrower, and deliberately so: only these two came from the parser, which is what decides
        // whether a finding is projected as a parser message or a style one.
        Assert.False(RuleIds.IsParseDiagnostic(RuleIds.CheckFailed));
    }
}
