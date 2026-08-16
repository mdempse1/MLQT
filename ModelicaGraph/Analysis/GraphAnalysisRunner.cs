using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Runs the enabled graph analyzers, stamps the configured severity on their findings, and applies
/// <c>__MLQT</c> suppression per target model — the graph-level counterpart to
/// <see cref="StyleChecking.RunStyleCheckingFindings"/>. Wired into the shared check session so the
/// GUI, CLI and MCP all get graph analyses with no per-surface work.
/// </summary>
public static class GraphAnalysisRunner
{
    /// <summary>The built-in graph analyzers. Grows as each analysis lands; empty means graph
    /// analysis is a no-op, so wiring it in is safe before any analyzer exists.</summary>
    public static readonly IReadOnlyList<IGraphAnalyzer> BuiltIn = new IGraphAnalyzer[]
    {
        new PackageOrderAnalyzer(),
        new UsesHygieneAnalyzer(),
    };

    public static List<Finding> Run(GraphAnalysisContext context, bool honorSuppressions = true)
        => Run(context, BuiltIn, honorSuppressions);

    /// <summary>
    /// True if any enabled built-in analyzer needs dependency analysis. A caller that builds the graph
    /// (the CLI, MCP) should run <c>GraphBuilder.AnalyzeDependenciesAsync</c> before checking when this
    /// returns true, and pass <c>dependenciesAnalyzed: true</c>.
    /// </summary>
    public static bool RequiresDependencyAnalysis(StyleCheckingSettings settings)
        => BuiltIn.Any(a => a.NeedsDependencyAnalysis
            && a.RuleIds.Any(id => settings.SeverityFor(id) != RuleSeverity.Off));

    public static List<Finding> Run(
        GraphAnalysisContext context, IReadOnlyList<IGraphAnalyzer> analyzers, bool honorSuppressions = true)
    {
        var findings = new List<Finding>();
        foreach (var analyzer in analyzers)
        {
            if (!analyzer.RuleIds.Any(id => context.Settings.SeverityFor(id) != RuleSeverity.Off))
                continue;
            // A dependency-requiring analyzer must not run without the edges — that would flag
            // everything as unreferenced. Skip it rather than emit false positives.
            if (analyzer.NeedsDependencyAnalysis && !context.DependenciesAnalyzed)
                continue;
            findings.AddRange(analyzer.Analyze(context));
        }

        if (findings.Count == 0)
            return findings;

        // Stamp the configured severity (visitors/analyzers emit at the record default).
        for (int i = 0; i < findings.Count; i++)
        {
            var sev = context.Settings.SeverityFor(findings[i].RuleId);
            if (sev == RuleSeverity.Off)
                sev = RuleCatalog.DefaultSeverityFor(findings[i].RuleId);
            findings[i] = findings[i] with { Severity = sev };
        }

        return honorSuppressions ? ApplySuppressions(context.Graph, findings) : findings;
    }

    // Drop findings the author waived via __MLQT annotations. Graph findings can span many models,
    // so parse each concerned model once (grouped) to read its suppression directives.
    private static List<Finding> ApplySuppressions(DirectedGraph graph, List<Finding> findings)
    {
        var kept = new List<Finding>();
        foreach (var group in findings.GroupBy(f => f.ModelId, StringComparer.Ordinal))
        {
            var suppressions = BuildSuppressions(graph, group.Key);
            foreach (var finding in group)
                if (suppressions is null || !suppressions.IsSuppressed(finding))
                    kept.Add(finding);
        }
        return kept;
    }

    private static SuppressionSet? BuildSuppressions(DirectedGraph graph, string modelId)
    {
        var parsed = graph.GetNode<ModelNode>(modelId)?.Definition?.EnsureParsed();
        if (parsed is null)
            return null;

        var lastDot = modelId.LastIndexOf('.');
        var basePackage = lastDot > 0 ? modelId[..lastDot] : string.Empty;
        var extractor = new MlqtSuppressionExtractor(basePackage);
        extractor.VisitStored_definition(parsed);
        var set = extractor.Build();
        return set.IsEmpty ? null : set;
    }
}
