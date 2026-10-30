using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.DataTypes;
using MLQT.Services.Interfaces;
using ModelicaParser.Helpers;

namespace MLQT.McpServer.Helpers;

/// <summary>The outcome of resolving a connection endpoint (e.g. "sine1.y") to its connector class.</summary>
internal sealed record PortResolution(ModelNode? Connector, string? Error, string? Note);

/// <summary>
/// Resolves a connection endpoint to its connector class and decides whether two endpoints are
/// compatible — used by mlqt_edit_class's add_connection to refuse obviously wrong wiring without rejecting valid signal
/// connections. Compatibility is by structural SIGNATURE (ignoring input/output causality), so a
/// RealOutput and a RealInput match (both are a single Real) while a signal port and a physical Pin do
/// not. When a type cannot be resolved the check is inconclusive (a note, not a refusal).
/// </summary>
internal static class ConnectorCompatibility
{
    /// <summary>
    /// Resolve a dotted port reference (component[.subcomponent...].connector, subscripts allowed) to
    /// the connector class at its end.
    /// </summary>
    /// <remarks>
    /// Resolved as <c>MLQT.Connection.Incompatible</c> resolves it - <c>ClassElementResolver.ReferencesIn</c>,
    /// then <see cref="ConnectorSignature.ConnectorOf"/> - so the tool refuses exactly the connections
    /// the rule would report. Its own walk split <c>a[1].p</c> into a segment <c>a[1]</c>, which no
    /// component is called, and refused a valid array connect; it judged a replaceable port, which the
    /// rule leaves to the redeclaration. The walk below now only words the error when the reference
    /// does not resolve.
    /// </remarks>
    public static PortResolution ResolvePort(ILibraryDataService libraries, string classId, string portRef)
    {
        if (libraries.GetModelById(classId) is not { } owner)
            return new PortResolution(null, null, $"could not resolve '{portRef}' (class '{classId}' not loaded)");

        var graph = libraries.CombinedGraph;
        if (ClassElementResolver.ReferencesIn(graph, owner).Resolve(portRef) is { } resolved)
        {
            if (resolved.Type is null)
                return new PortResolution(null, null,
                    $"the type '{resolved.Element.Element.Type}' of '{resolved.Element.Element.Name}' does not resolve to a loaded class — " +
                    "check the name or load its library (e.g. the Modelica Standard Library)");

            return ConnectorSignature.ConnectorOf(graph, resolved) is { } connector
                ? new PortResolution(connector, null, null)
                : new PortResolution(null, null, $"'{portRef}' is replaceable, so a redeclaration decides what it connects to");
        }

        return Explain(libraries, classId, portRef);
    }

    /// <summary>Why a port reference did not resolve, segment by segment.</summary>
    private static PortResolution Explain(ILibraryDataService libraries, string classId, string portRef)
    {
        var segments = ModelicaName.Segments(portRef)
            .Select(s => StripSubscripts(s).Trim())
            .Where(s => s.Length > 0)
            .ToArray();
        if (segments.Length == 0)
            return new PortResolution(null, $"'{portRef}' is not a valid port reference.", null);

        var signatures = new ConnectorSignature(libraries.CombinedGraph);
        var currentId = classId;
        for (var i = 0; i < segments.Length; i++)
        {
            var owner = libraries.GetModelById(currentId);
            if (owner is null)
                return new PortResolution(null, null, $"could not resolve '{portRef}' (class '{currentId}' not loaded)");

            // A signal on an expandable bus is declared by connecting it, which is what this edit is
            // doing - so MLQT.Connection.Incompatible accepts it, and refusing it here broke this
            // tool's promise to refuse only what the rule reports.
            if (i > 0 && signatures.IsExpandable(owner))
                return new PortResolution(null, null,
                    $"'{segments[i - 1]}' is an expandable connector ('{currentId}'), so '{segments[i]}' is declared by connecting it");

            var member = ClassElementResolver
                .Collect(libraries.CombinedGraph, owner, includeProtected: true, includeInherited: true)
                .FirstOrDefault(m => m.Element.Kind == ClassElementKind.Component &&
                                     string.Equals(m.Element.Name, segments[i], StringComparison.Ordinal));
            if (member is null)
                return new PortResolution(null, $"'{segments[i]}' is not a component of '{currentId}'.", null);

            var typeNode = TypeResolver.Resolve(libraries.CombinedGraph, member.OwnerId, member.Element.Type, member.OwnerImports);
            if (typeNode is null)
                return new PortResolution(null, null,
                    $"the type '{member.Element.Type}' of '{segments[i]}' does not resolve to a loaded class — " +
                    "check the name or load its library (e.g. the Modelica Standard Library)");

            if (i == segments.Length - 1)
                break;

            currentId = typeNode.Id; // descend into the component's class
        }

        // Every segment found here, and still not resolvable as the rule resolves it: say so rather
        // than guess.
        return new PortResolution(null, null, $"could not resolve '{portRef}'");
    }

    private static string StripSubscripts(string segment) =>
        segment.IndexOf('[') is var at and >= 0 && !segment.StartsWith('\'') ? segment[..at] : segment;

    /// <summary>
    /// Whether two connectors may be joined: false only when both structural signatures are known and
    /// differ, input/output causality aside. Worked out with one set of caches for the pair.
    /// </summary>
    /// <remarks>
    /// <see cref="ConnectorSignature"/>, the definition <c>MLQT.Connection.Incompatible</c> uses, so
    /// this tool and the rule cannot disagree about which connections are wrong. This file had its own
    /// until phase 2, which read an alias connector (<c>connector Frame_a = Frame</c>) as the text
    /// <c>alias:Frame</c> - never equal to Frame's own signature, so wiring an alias to its base was
    /// refused - and ignored <c>stream</c>.
    /// </remarks>
    public static bool MayConnect(ILibraryDataService libraries, ModelNode a, ModelNode b)
    {
        var signatures = new ConnectorSignature(libraries.CombinedGraph);
        return signatures.Of(a) is not { } sigA
               || signatures.Of(b) is not { } sigB
               || string.Equals(sigA, sigB, StringComparison.Ordinal);
    }
}
