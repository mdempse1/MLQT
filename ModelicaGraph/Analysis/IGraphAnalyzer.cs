using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// A whole-graph static analysis (Phase 6). Unlike a per-class style rule (which sees only one
/// class's parse tree), a graph analyzer reasons across the <see cref="DirectedGraph"/> — cross-model
/// dependency edges, inheritance chains, package structure — and attributes each <see cref="Finding"/>
/// to the model it concerns. Findings flow into the same stream as per-class findings, so they share
/// severity stamping, the baseline fingerprint, <c>__MLQT</c> suppression and every output format.
/// </summary>
public interface IGraphAnalyzer
{
    /// <summary>
    /// The analysis's name: in the finding that reports it failed, whose fingerprint it is part of, and
    /// in the run's timings. <b>Written out, never <c>GetType().Name</c></b>: an obfuscated build renames
    /// the type, and a name that moves with it changes the fingerprint, so a baseline recorded by one
    /// build stops matching the same finding in another without a word. A built-in analyzer's name is
    /// its class name as it was when fingerprints were first recorded, and must not change.
    /// </summary>
    string Name { get; }

    /// <summary>The rule ids this analyzer can emit. The runner skips the analyzer entirely when none
    /// of these is enabled, so an analyzer with expensive prerequisites costs nothing when off.</summary>
    IReadOnlyList<string> RuleIds { get; }

    /// <summary>True if this analyzer relies on cross-model dependency edges
    /// (<c>UsedModelIds</c>/<c>UsedByModelIds</c>). The runner skips it unless
    /// <see cref="GraphAnalysisContext.DependenciesAnalyzed"/> is set, so the caller must run
    /// dependency analysis first (see <see cref="GraphAnalysisRunner.RequiresDependencyAnalysis"/>).</summary>
    bool NeedsDependencyAnalysis => false;

    /// <summary>Produce findings for the given graph. Severity is left at the record default and
    /// stamped by the runner; suppression is applied by the runner.</summary>
    IEnumerable<Finding> Analyze(GraphAnalysisContext context);
}
