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
    /// Resolve a dotted port reference (component[.subcomponent...].connector) to the connector class at
    /// its end, walking component types through the graph.
    /// </summary>
    public static PortResolution ResolvePort(ILibraryDataService libraries, string classId, string portRef)
    {
        var segments = ModelicaName.Segments(portRef).Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
        if (segments.Length == 0)
            return new PortResolution(null, $"'{portRef}' is not a valid port reference.", null);

        var currentId = classId;
        for (var i = 0; i < segments.Length; i++)
        {
            var owner = libraries.GetModelById(currentId);
            if (owner is null)
                return new PortResolution(null, null, $"could not resolve '{portRef}' (class '{currentId}' not loaded)");

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
                return new PortResolution(typeNode, null, null);

            currentId = typeNode.Id; // descend into the component's class
        }

        return new PortResolution(null, $"could not resolve '{portRef}'.", null);
    }

    /// <summary>
    /// A structural signature of a connector, ignoring input/output causality, or null when MLQT
    /// cannot tell what it is. Two connectors are compatible when their signatures are equal.
    /// </summary>
    /// <remarks>
    /// <see cref="ConnectorSignature"/>, the definition <c>MLQT.Connection.Incompatible</c> uses, so
    /// this tool and the rule cannot disagree about which connections are wrong. This file had its own
    /// until phase 2, which read an alias connector (<c>connector Frame_a = Frame</c>) as the text
    /// <c>alias:Frame</c> - never equal to Frame's own signature, so wiring an alias to its base was
    /// refused - and ignored <c>stream</c>.
    /// </remarks>
    public static string? Signature(ILibraryDataService libraries, ModelNode connector) =>
        new ConnectorSignature(libraries.CombinedGraph).Of(connector);

    public static bool SignaturesCompatible(string? a, string? b)
        => a is not null && b is not null && string.Equals(a, b, StringComparison.Ordinal);
}
