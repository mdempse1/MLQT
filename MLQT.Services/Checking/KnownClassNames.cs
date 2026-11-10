using System.Collections.Concurrent;
using ModelicaGraph;
using ModelicaParser.Helpers;

namespace MLQT.Services.Checking;

/// <summary>
/// The class names a class's descriptions and documentation may use as words: those of its own
/// library and of the libraries its library declares in <c>uses(...)</c>.
/// </summary>
/// <remarks>
/// <para><b>Not every class loaded.</b> It was: the leaf name of every class in the graph counted as a
/// correctly spelled word, so what was flagged depended on what else happened to be loaded. With a
/// tool's library folder loaded for reference - some 43,000 classes of fifty libraries - words such as
/// <c>Kv</c>, <c>MPa</c>, <c>rgb</c> and <c>Aluminium</c> were accepted in MSL because a vendor library
/// had a class of that name, and the desktop app, the CLI and the MCP server reported 64 findings
/// fewer or more for the same library depending on which of them had the folder loaded.</para>
///
/// <para>A library's own names and those it declares it uses are the ones its prose can mean: a
/// reference to a class of a library it does not use is not a reference its code can make. They do
/// not depend on what else is loaded, beyond the declared libraries themselves.</para>
/// </remarks>
internal static class KnownClassNames
{
    /// <summary>
    /// A lookup from a class's full id to the names its text may use, answered once per library and
    /// shared by every class checked in the run.
    /// </summary>
    public static Func<string, IReadOnlySet<string>> For(DirectedGraph graph)
    {
        // Leaf names per top-level library, in one pass. Ordinal: Modelica is case sensitive.
        var byLibrary = graph.ModelNodes
            .GroupBy(n => ModelicaName.RootLibraryOf(n.Id), StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(n => ModelicaName.LeafOf(n.Id)).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        var perLibrary = new ConcurrentDictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal);

        return modelId => perLibrary.GetOrAdd(ModelicaName.RootLibraryOf(modelId), library =>
        {
            var names = byLibrary.TryGetValue(library, out var own)
                ? new HashSet<string>(own, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            if (graph.GetNode<ModelicaGraph.DataTypes.ModelNode>(library)?.Uses is { } uses)
            {
                foreach (var used in uses.Keys)
                {
                    if (byLibrary.TryGetValue(used, out var theirs))
                        names.UnionWith(theirs);
                }
            }

            return names;
        });
    }
}
