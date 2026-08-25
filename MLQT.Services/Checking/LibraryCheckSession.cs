using ModelicaGraph;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Checking;

/// <summary>
/// Facade over the style-checking pipeline: builds the per-library <see cref="StyleCheckContext"/>
/// once and runs the structured findings check across a set of models in parallel. Shared by the
/// MCP server and the CLI so there is a single load → check implementation.
///
/// Results are returned in an unspecified order (parallel); callers that need deterministic output
/// should sort (e.g. by model id, then line, then rule id).
/// </summary>
public static class LibraryCheckSession
{
    public static IReadOnlyList<Finding> Check(
        DirectedGraph graph,
        IEnumerable<ModelNode> models,
        StyleCheckingSettings settings,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        bool honorSuppressions = true,
        bool? dependenciesAnalyzed = null,
        string? repositoryRoot = null)
    {
        // Classes recovered from an encrypted library's documentation are dropped here, at the one
        // place every surface goes through, rather than left to each caller to remember. They are
        // loaded so that references into them resolve — not so they can be judged: their "source"
        // is MLQT's own reconstruction, so any finding would be about the reconstruction, and it
        // would name a third-party library the user cannot edit in any case.
        var modelList = (models as IReadOnlyList<ModelNode> ?? models.ToList())
            .Where(node => node is null || !node.IsExternalStub)
            .ToList();

        // Parse diagnostics come first and are not gated by the severity map. A class that failed to
        // parse is one the style rules below either skip outright (a placeholder) or read only partly,
        // so reporting the style result without the parse error would understate the problem — and
        // "no rules enabled" still has to report a file that cannot be read.
        var parseFindings = ParserErrorReporter.ToFindings(modelList);

        if (!settings.HasAnyStyleRuleEnabled)
            return parseFindings;

        var context = StyleCheckContext.Build(
            settings, graph, customDictionary, dictionaryManager, repositoryRoot);
        var all = new System.Collections.Concurrent.ConcurrentBag<Finding>();

        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) };
        Parallel.ForEach(modelList, options, node =>
        {
            if (node is null || node.IsParseFailurePlaceholder)
                return;

            try
            {
                foreach (var finding in StyleCheckRunner.RunFindings(node, settings, context, honorSuppressions))
                    all.Add(finding);
            }
            catch
            {
                // Skip models that fail to check — don't stall the whole run.
            }
        });

        var results = all.ToList();
        results.AddRange(parseFindings);

        // Whole-graph analyses (Phase 6): run once over the checked model set and merge. A no-op until
        // graph analyzers are registered and their rules enabled, so it never affects a per-class-only run.
        var checkable = modelList.Where(m => m is not null && !m.IsParseFailurePlaceholder).ToList();
        var graphContext = new GraphAnalysisContext(graph, settings, checkable, dependenciesAnalyzed);
        results.AddRange(GraphAnalysisRunner.Run(graphContext, honorSuppressions));

        return results;
    }
}
