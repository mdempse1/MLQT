using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Seam for suppressing findings the author has intentionally accepted — the insertion point for
/// the later <c>__MLQT</c> vendor-annotation suppression. Phase 1 ships only the no-op
/// implementation; the pipeline always routes findings through a suppressor so a real one can be
/// dropped in without changing the orchestration.
/// </summary>
public interface IFindingSuppressor
{
    IReadOnlyList<Finding> Apply(IReadOnlyList<Finding> findings);
}

/// <summary>Pass-through suppressor. The Phase 1 default.</summary>
public sealed class NoOpFindingSuppressor : IFindingSuppressor
{
    public static readonly NoOpFindingSuppressor Instance = new();
    public IReadOnlyList<Finding> Apply(IReadOnlyList<Finding> findings) => findings;
}
