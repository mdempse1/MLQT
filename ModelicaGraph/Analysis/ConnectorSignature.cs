using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// A connector's structural signature, which two connectors must share to be connected: the one
/// definition <c>MLQT.Connection.Incompatible</c> and the MCP server's <c>add_connection</c> both use.
/// </summary>
/// <remarks>
/// <para><b>What it compares.</b> A connector built from a value - <c>connector RealInput = input Real</c>,
/// or one whose type is <c>SI.Voltage</c> - is that value's predefined type, so a <c>RealOutput</c> and
/// a <c>VoltageInput</c> are both <c>Real</c> and may be connected; causality (<c>input</c>/<c>output</c>)
/// is a separate question. An enumeration is its own type. A connector with members is their names,
/// each with its <c>flow</c> or <c>stream</c> prefix, inherited members included: <c>Pin</c> is
/// <c>i (flow), v</c> and <c>HeatPort</c> is <c>Q_flow (flow), T</c>. Member types are not compared,
/// which leaves the check lenient: what it reports is two connectors with different members, and that
/// is what a wrong connection usually is.</para>
///
/// <para><b>An alias is its base.</b> <c>connector Frame_a = Frame</c> has Frame's members. The MCP
/// server's version read it as the text <c>alias:Frame</c>, which no other connector's signature could
/// ever equal - harmless while it only advised one tool, and a false finding on every connection
/// between an alias and its base had a rule been built on it.</para>
///
/// <para><b>Three states.</b> No signature - null - is "cannot tell": the connector is not loaded, is
/// encrypted, has a base MLQT cannot read, or is expandable, whose members are whatever is connected
/// to it. A rule compares only two signatures it has.</para>
/// </remarks>
public sealed class ConnectorSignature(DirectedGraph graph)
{
    private readonly TypeResolver.AncestorCache _ancestors = new();
    private readonly ClassElementResolver.InterfaceCache _interfaces = new();
    private readonly ConcurrentDictionary<string, string?> _signatures = new(StringComparer.Ordinal);
    private ClassCompleteness? _completeness;

    internal TypeResolver.AncestorCache Ancestors => _ancestors;
    internal ClassElementResolver.InterfaceCache Interfaces => _interfaces;

    private ClassCompleteness Completeness =>
        LazyInitializer.EnsureInitialized(ref _completeness, () => new ClassCompleteness(graph, _ancestors));

    /// <summary>The connector's signature, or null when MLQT cannot tell what it is.</summary>
    public string? Of(ModelNode connector) => Of(connector, 0);

    /// <summary>Whether a signature describes a connector with members, as opposed to a value.</summary>
    public static bool HasMembers(string signature) => signature.StartsWith("members:", StringComparison.Ordinal);

    /// <summary>A signature as a reader would want it named: its members, or its value type.</summary>
    public static string Describe(string signature) =>
        signature[(signature.IndexOf(':') + 1)..] is { Length: > 0 } rest ? rest : "nothing";

    private string? Of(ModelNode node, int depth)
    {
        if (depth > 16 || node.IsExternalStub || node.IsParseFailurePlaceholder)
            return null;
        if (_signatures.TryGetValue(node.Id, out var known))
            return known;

        var signature = Compute(node, depth);
        _signatures[node.Id] = signature;
        return signature;
    }

    private string? Compute(ModelNode node, int depth)
    {
        var (isShort, isEnumeration) = node.Definition.Borrow(
            tree => tree.class_definition() is [var c, ..] && c.class_specifier()?.short_class_specifier() is { } spec
                ? (true, spec.GetChild(2)?.GetText() == "enumeration")
                : (false, false),
            (false, false));

        if (isEnumeration)
            return "enumeration:" + node.Id;

        var bases = ClassImports.BasesOf(node.Definition);
        if (isShort && bases.Count == 1)
        {
            var written = bases[0].Trim().TrimStart('.');
            if (TypeResolver.IsPredefined(written))
                return "value:" + written;

            return ClassElementResolver.ResolveBaseOf(graph, node, bases[0], ClassImports.For(node.Definition), _ancestors) is { } baseNode
                   && baseNode.Id != node.Id
                ? Of(baseNode, depth + 1)
                : null;
        }

        if (!Completeness.IsComplete(node) || Completeness.IsExpandable(node))
            return null;

        var members = ClassElementResolver
            .Collect(graph, node, includeProtected: false, includeInherited: true, _interfaces)
            .Where(m => m.Element.Kind == ClassElementKind.Component)
            .Select(m => m.Element.Connection is { Length: > 0 } connection ? $"{m.Element.Name} ({connection})" : m.Element.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // A "connector" with no members at all is a partial base or something MLQT misread; there is
        // nothing to compare.
        return members.Count == 0 ? null : "members:" + string.Join(", ", members);
    }
}
