namespace ModelicaGraph.DataTypes;

/// <summary>
/// What one class contributes to the coverage figures: whether it complies, and how much of it was
/// eligible to. Every dimension is a count, so any scope's coverage is the sum of its classes' facts
/// — which is what makes measuring a class once and keeping the answer worth doing.
///
/// <para>Deliberately not derived from findings. Coverage reports the true state whatever rules are
/// enabled and whatever has been suppressed, and findings carry neither the denominators nor the
/// gaps a waiver hid.</para>
///
/// <para>Held on <see cref="ModelDefinition"/> beside the parse tree and dropped when the source is
/// replaced, so a stale measurement cannot outlive the code it describes.</para>
/// </summary>
public sealed record CoverageFacts(
    bool HasDescription,
    bool HasIcon,
    int Components,
    int ParameterTotal,
    int ParametersWithDescription,
    int RealTotal,
    int RealWithUnit);
