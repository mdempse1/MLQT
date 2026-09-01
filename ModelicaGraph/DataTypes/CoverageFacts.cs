namespace ModelicaGraph.DataTypes;

/// <summary>
/// What one class contributes to the coverage figures: whether it complies, and how much of it was
/// eligible to. Every dimension is a count, so any scope's coverage is the sum of its classes' facts
/// — which is what makes measuring a class once and keeping the answer worth doing.
///
/// <para>Deliberately not derived from findings. Coverage reports the true state whatever rules are
/// enabled and whatever has been suppressed, and findings carry neither the denominators nor the
/// gaps a waiver hid. Which dimensions a class is measured for is a separate question, answered by
/// its repository's settings: measuring what nobody asked to see costs a tree walk per class.</para>
///
/// <para>Held on <see cref="ModelDefinition"/> beside the parse tree and dropped when the source is
/// replaced, so a stale measurement cannot outlive the code it describes.</para>
/// </summary>
/// <param name="Measured">Which dimensions these facts actually answer for. A cached measurement is
/// reused only when it covers everything the caller now needs, so widening the settings re-measures
/// rather than reading zeros as gaps.</param>
/// <param name="Failed">The layout dimensions this class violates. Only meaningful within
/// <paramref name="Measured"/>; every layout dimension measured and not failed is compliant.</param>
public sealed record CoverageFacts(
    bool HasDescription,
    bool HasIcon,
    int Components,
    int ParameterTotal,
    int ParametersWithDescription,
    int RealTotal,
    int RealWithUnit,
    bool HasDocumentationInfo = false,
    bool HasDocumentationRevisions = false,
    int ConstantTotal = 0,
    int ConstantsWithDescription = 0,
    CoverageDimension Measured = CoverageDimension.All,
    CoverageDimension Failed = CoverageDimension.None);
