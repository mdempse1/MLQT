using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;
using MLQT.Services.Helpers;

namespace MLQT.Cli;

/// <summary>
/// Records a coverage snapshot into the same <c>.mlqt/metrics-history.json</c> the desktop app's
/// Coverage dashboard reads, so a CI run per commit builds the burndown automatically instead of
/// relying on someone remembering to press "Save snapshot".
/// </summary>
internal static class MetricsRecorder
{
    /// <summary>
    /// Computes the coverage metrics for the checked models and appends a point.
    ///
    /// <paramref name="force"/> bypasses the "only if it changed" rule. Leave it off in CI: skipping
    /// an unchanged point is what stops a job that commits the history file from re-triggering itself
    /// in a loop.
    /// </summary>
    public static void Record(
        string path,
        DirectedGraph graph,
        IReadOnlyList<ModelNode> models,
        IReadOnlyList<Finding> findings,
        DateTime timestampUtc,
        VcsStamp stamp,
        bool force,
        TextWriter stderr)
    {
        try
        {
            var metrics = MetricsCalculator.Compute(graph, models);

            // Match the dashboard's figure: active style findings only. Parse diagnostics are not
            // style debt and would make the trend jump on a syntax error rather than on quality.
            var violations = findings.Count(f => !RuleIds.IsParseDiagnostic(f.RuleId));

            // Scope "" — the whole checked set, which is what `mlqt check <root>` covers and what the
            // dashboard's "all libraries" view reads.
            var snapshot = MetricsSnapshot.From(
                metrics, scope: "", timestampUtc, violations, stamp.Revision, stamp.Branch);

            if (force)
            {
                MetricsHistoryStore.Append(path, snapshot);
                stderr.WriteLine($"note: recorded metrics snapshot in {path} (forced)");
                return;
            }

            var (outcome, _) = MetricsHistoryStore.AppendIfChanged(path, snapshot);
            stderr.WriteLine(outcome switch
            {
                MetricsHistoryStore.AppendOutcome.Appended =>
                    $"note: recorded metrics snapshot in {path}",
                MetricsHistoryStore.AppendOutcome.RevisionAlreadyRecorded =>
                    $"note: metrics unchanged — {path} already has a point for this revision",
                _ =>
                    $"note: metrics unchanged since the last point — {path} not modified",
            });
        }
        catch (Exception ex)
        {
            // Recording is an extra, not the job. A check that found real problems must still report
            // them and return its own exit code if the history file cannot be written.
            stderr.WriteLine($"warning: could not record metrics: {ex.Message}");
        }
    }
}
