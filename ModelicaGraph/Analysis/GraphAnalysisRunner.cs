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
        new UnusedClassAnalyzer(),
        new ShadowingAnalyzer(),
        new UnusedMembersAnalyzer(),
        new UnusedImportAnalyzer(),
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

        // Drop excluded libraries from the REPORTED set only. They stay in context.Graph, so a test
        // library still counts as a user of the classes it exercises — excluding it must not make the
        // library under test look unused.
        if (context.Settings.ExcludedLibraries.Count > 0)
        {
            var reportable = context.Models.Where(m => !context.Settings.IsLibraryExcluded(m.Id)).ToList();
            if (reportable.Count != context.Models.Count)
                context = new GraphAnalysisContext(
                    context.Graph, context.Settings, reportable, context.DependenciesAnalyzed);
        }

        foreach (var analyzer in analyzers)
        {
            if (!analyzer.RuleIds.Any(id => context.Settings.SeverityFor(id) != RuleSeverity.Off))
                continue;
            // A dependency-requiring analyzer must not run without the edges — that would flag
            // everything as unreferenced. Skip it rather than emit false positives.
            if (analyzer.NeedsDependencyAnalysis && !context.DependenciesAnalyzed)
                continue;

            try
            {
                findings.AddRange(analyzer.Analyze(context));
            }
            catch (Exception ex)
            {
                // The same bargain the per-class check already makes: one analysis that cannot finish
                // must not take the others with it, and must not vanish either. Before this, the CLI
                // died on the exception (exit code and stack trace, not the documented 2) while the
                // desktop app caught it several frames up and dropped every graph finding for every
                // repository in silence — two different wrong answers to the same question.
                findings.Add(Failure(context, analyzer, ex));
            }
        }

        if (findings.Count == 0)
            return findings;

        // Stamp the configured severity (visitors/analyzers emit at the record default). A diagnostic
        // is not in the map and is not stamped from it — it keeps the severity it was created with.
        for (int i = 0; i < findings.Count; i++)
        {
            if (RuleIds.IsDiagnostic(findings[i].RuleId))
                continue;

            var sev = context.Settings.SeverityFor(findings[i].RuleId);
            if (sev == RuleSeverity.Off)
                sev = RuleCatalog.DefaultSeverityFor(findings[i].RuleId);
            findings[i] = findings[i] with { Severity = sev };
        }

        return honorSuppressions ? ApplySuppressions(context.Graph, findings) : findings;
    }

    /// <summary>
    /// Reports an analysis that threw, as the diagnostic that says the results are incomplete.
    /// Attributed to the root of the checked set, because a whole-graph analysis belongs to no one
    /// class — and a finding needs a model id a reader can navigate to.
    /// </summary>
    private static Finding Failure(GraphAnalysisContext context, IGraphAnalyzer analyzer, Exception ex) =>
        new()
        {
            RuleId = RuleIds.CheckFailed,
            ModelId = OwnerOf(context),
            Discriminator = analyzer.GetType().Name,
            Message = $"The {analyzer.GetType().Name} analysis failed " +
                      $"({ex.GetType().Name}: {ex.Message}). Its findings are missing from these results.",
            Severity = RuleSeverity.Error,
        };

    /// <summary>
    /// The class a whole-graph failure is reported against: the shortest top-level package in the
    /// checked set, else the first model, else nothing. Any of them is arbitrary; a stable choice is
    /// what matters, so the fingerprint does not move between runs over the same library.
    /// </summary>
    private static string OwnerOf(GraphAnalysisContext context)
    {
        if (context.Models.Count == 0)
            return string.Empty;

        var root = context.Models
            .Where(m => !m.Id.Contains('.'))
            .OrderBy(m => m.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        return root?.Id ?? context.Models.OrderBy(m => m.Id, StringComparer.Ordinal).First().Id;
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

    /// <summary>
    /// The suppression directives on one class, or null when there are none to read. Reading them
    /// means re-parsing the class, so it is per-model on purpose: a class that will not parse costs
    /// its own waivers and no one else's, and the findings come through unsuppressed — visible rather
    /// than silently dropped, which is the safe direction for a check to fail in.
    /// </summary>
    private static SuppressionSet? BuildSuppressions(DirectedGraph graph, string modelId)
    {
        modelicaParser.Stored_definitionContext? parsed;
        try
        {
            parsed = graph.GetNode<ModelNode>(modelId)?.Definition?.EnsureParsed();
        }
        catch
        {
            return null;
        }

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
