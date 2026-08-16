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

    public GraphAnalysisContext(
        DirectedGraph graph, StyleCheckingSettings settings, IReadOnlyList<ModelNode> models,
        bool dependenciesAnalyzed = false)
    {
        Graph = graph;
        Settings = settings;
        Models = models;
        DependenciesAnalyzed = dependenciesAnalyzed;
    }
}
