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
        bool dependenciesAnalyzed = false)
    {
        if (!settings.HasAnyStyleRuleEnabled)
            return [];

        var modelList = models as IReadOnlyList<ModelNode> ?? models.ToList();
        var context = StyleCheckContext.Build(settings, graph, customDictionary, dictionaryManager);
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

        // Whole-graph analyses (Phase 6): run once over the checked model set and merge. A no-op until
        // graph analyzers are registered and their rules enabled, so it never affects a per-class-only run.
        var checkable = modelList.Where(m => m is not null && !m.IsParseFailurePlaceholder).ToList();
        var graphContext = new GraphAnalysisContext(graph, settings, checkable, dependenciesAnalyzed);
        results.AddRange(GraphAnalysisRunner.Run(graphContext, honorSuppressions));

        return results;
    }
}
