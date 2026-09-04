using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using MLQT.Services.Checking;
using MLQT.Services.Helpers;
using ModelicaParser.Helpers;

namespace MLQT.Cli;

/// <summary>
/// Records a coverage snapshot into the same <c>.mlqt/metrics-history.json</c> the desktop app's
/// Metrics tab reads, so a CI run per commit builds the burndown automatically instead of
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
        StyleCheckingSettings settings,
        DateTime timestampUtc,
        VcsStamp stamp,
        bool force,
        TextWriter stderr)
    {
        try
        {
            // One point for the whole checked set, plus one per library. The per-library points are
            // what the dashboard's scope filter reads: it matches a snapshot's Scope against the
            // selected package id exactly, so a repository checked only under the empty scope shows
            // current coverage for a library but an empty trend.
            var scopes = new List<(string Scope, IReadOnlyList<ModelNode> Models)>
            {
                ("", models)
            };
            // Only top-level PACKAGES get their own scope. The dashboard's scope picker offers
            // packages, so a scope recorded for anything else could never be selected — and a flat
            // folder of loose .mo files would otherwise produce one scope per class.
            var libraryRoots = models
                .Where(m => m.ClassType == "package" && !m.Id.Contains('.'))
                .Select(m => m.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(root => root, StringComparer.Ordinal)
                .ToList();

            scopes.AddRange(libraryRoots.Select(root => (
                Scope: root,
                Models: (IReadOnlyList<ModelNode>)models
                    .Where(m => ModelicaName.RootLibraryOf(m.Id) == root)
                    .ToList())));

            var recorded = new List<string>();
            var skipped = 0;

            foreach (var (scope, scopeModels) in scopes)
            {
                // The run's own settings decide the dimensions, so a point recorded by CI carries
                // the same rows the desktop dashboard shows for the same library — a trend whose
                // dimensions changed with who wrote the point would be unreadable.
                var metrics = MetricsCalculator.Compute(graph, scopeModels, _ => settings);

                // Match the dashboard's figure: active style findings in scope. A diagnostic is not
                // style debt — it would make the trend jump on a syntax error, or on a defect in MLQT,
                // rather than on the library's quality moving.
                var inScope = scopeModels.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
                var findingCount = findings.Count(f =>
                    !RuleIds.IsDiagnostic(f.RuleId) && inScope.Contains(f.ModelId));

                var snapshot = MetricsSnapshot.From(
                    metrics, scope, timestampUtc, findingCount, stamp.Revision, stamp.Branch);

                if (force)
                {
                    MetricsHistoryStore.Append(path, snapshot);
                    recorded.Add(Label(scope));
                    continue;
                }

                if (MetricsHistoryStore.AppendIfChanged(path, snapshot).Outcome
                    == MetricsHistoryStore.AppendOutcome.Appended)
                    recorded.Add(Label(scope));
                else
                    skipped++;
            }

            if (recorded.Count > 0)
                stderr.WriteLine(
                    $"note: recorded metrics for {string.Join(", ", recorded)} in {path}" +
                    (force ? " (forced)" : ""));
            if (skipped > 0)
                stderr.WriteLine(
                    $"note: {skipped} metrics scope(s) unchanged since the last point — not re-recorded");
        }
        catch (Exception ex)
        {
            // Recording is an extra, not the job. A check that found real problems must still report
            // them and return its own exit code if the history file cannot be written.
            stderr.WriteLine($"warning: could not record metrics: {ex.Message}");
        }
    }

    private static string Label(string scope) => scope.Length == 0 ? "all libraries" : scope;
}
