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

    public GraphAnalysisContext(DirectedGraph graph, StyleCheckingSettings settings, IReadOnlyList<ModelNode> models)
    {
        Graph = graph;
        Settings = settings;
        Models = models;
    }
}
