using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// What a component's parameters are actually set to: the modification the instance was given,
/// falling back to the default its type declares.
///
/// <para>Two questions need this and they are the same question. An icon labelled <c>J=%J</c> has to
/// say what J is (B278), and a connector declared <c>heatPort if useHeatPort</c> is only there when
/// that parameter is true (B277). Both are "what is this parameter, for this instance", and both
/// read the same two places in the same order.</para>
///
/// <para><b>Scalar modifications only.</b> <c>inertia1(J=1, phi(fixed=true))</c> sets J and
/// configures a sub-component; the second is not a value this component takes and is not reported.
/// A name with no answer comes back null, which both callers treat as "not known" rather than as a
/// value.</para>
/// </summary>
public static class ComponentValues
{
    /// <summary>
    /// A lookup over the parameters of <paramref name="type"/> as this instance has them.
    /// </summary>
    /// <param name="graph">The graph the type's own declarations are resolved through.</param>
    /// <param name="type">The component's type, or null when it did not resolve — the instance's own
    /// modifications still answer, which is more than nothing.</param>
    /// <param name="instanceModifications">
    /// What the declaration gave it, from <see cref="ClassElement.Modifications"/>.
    /// </param>
    public static Func<string, string?> For(
        DirectedGraph graph, ModelNode? type, IReadOnlyDictionary<string, string>? instanceModifications)
    {
        ArgumentNullException.ThrowIfNull(graph);

        // Built once per component and read once per %reference and per condition, so the type's
        // defaults are collected lazily: most components carry neither.
        Dictionary<string, string>? defaults = null;
        var collected = false;

        return name =>
        {
            if (instanceModifications is not null && instanceModifications.TryGetValue(name, out var given))
                return given;

            if (type is null)
                return null;

            if (!collected)
            {
                collected = true;
                defaults = Defaults(graph, type);
            }

            return defaults is not null && defaults.TryGetValue(name, out var declared) ? declared : null;
        };
    }

    /// <summary>
    /// Every value the type declares for itself, inherited ones included — a conditional connector's
    /// governing parameter is usually declared in a base class rather than beside it.
    /// </summary>
    private static Dictionary<string, string>? Defaults(DirectedGraph graph, ModelNode type)
    {
        Dictionary<string, string>? values = null;

        foreach (var member in ClassElementResolver.Collect(
                     graph, type, includeProtected: true, includeInherited: true))
        {
            if (member.Element.Kind != ClassElementKind.Component)
                continue;
            if (member.Element.DefaultValue is not { Length: > 0 } value)
                continue;

            // The first answer wins: Collect reports a class's own declaration before the inherited
            // one it shadows, which is the order the values override in.
            (values ??= new Dictionary<string, string>(StringComparer.Ordinal)).TryAdd(member.Element.Name, value);
        }

        return values;
    }
}
