using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Inputs shared by all graph analyzers: the whole graph, the active settings, and the set of models
/// under check (so an analyzer can scope its work to what was requested rather than the entire graph).
/// </summary>
public sealed class GraphAnalysisContext
{
    public DirectedGraph Graph { get; }
    public StyleCheckingSettings Settings { get; }
    public IReadOnlyList<ModelNode> Models { get; }

    /// <summary>
    /// True when cross-model dependency analysis (<c>GraphBuilder.AnalyzeDependenciesAsync</c>) has run,
    /// so <c>UsedModelIds</c>/<c>UsedByModelIds</c> are populated. Analyzers that need those edges must
    /// not run when this is false — otherwise every model looks unreferenced (guaranteed false positives).
    /// </summary>
    public bool DependenciesAnalyzed { get; }

    /// <param name="dependenciesAnalyzed">Leave null to take the answer from
    /// <see cref="DirectedGraph.DependenciesAnalyzed"/>, which is the authoritative one. Pass a value
    /// only when the caller genuinely knows better than the graph (e.g. a test fixture that wires
    /// edges up by hand, or a run that deliberately suppresses the dependency-based analyzers).</param>
    public GraphAnalysisContext(
        DirectedGraph graph, StyleCheckingSettings settings, IReadOnlyList<ModelNode> models,
        bool? dependenciesAnalyzed = null)
    {
        Graph = graph;
        Settings = settings;
        Models = models;
        DependenciesAnalyzed = dependenciesAnalyzed ?? graph.DependenciesAnalyzed;
    }
}
