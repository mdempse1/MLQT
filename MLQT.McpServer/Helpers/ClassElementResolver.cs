using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// One element of a class after inheritance is merged in: the raw element, the id of the class that
/// actually declares it (<see cref="OwnerId"/>) and that class's imports (so its type can be resolved
/// in the right scope), plus <see cref="InheritedFrom"/> — null when the element is declared in the
/// queried class itself, otherwise the base class it was inherited from.
/// </summary>
internal sealed record ResolvedElement(
    ClassElement Element,
    string? InheritedFrom,
    string OwnerId,
    IReadOnlyList<string> OwnerImports);

/// <summary>
/// Collects the full element set of a class, following its <c>extends</c> clauses so inherited
/// parameters, connectors and other members are included — the complete picture the class presents,
/// not just what it declares directly. A derived declaration shadows a same-named inherited one, and
/// diamond inheritance is visited once. Imports and extends clauses themselves are reported only for
/// the queried class (they are not "inherited members").
/// </summary>
internal static class ClassElementResolver
{
    private const int MaxDepth = 32;

    private static readonly IReadOnlyDictionary<string, string> NoMods =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static List<ResolvedElement> Collect(
        ILibraryDataService libraries, ModelNode node, bool includeProtected, bool includeInherited)
    {
        var result = new List<ResolvedElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        Walk(libraries, node, includeProtected, includeInherited, origin: null, NoMods, result, seen, visited, depth: 0);
        return result;
    }

    private static void Walk(
        ILibraryDataService libraries, ModelNode node, bool includeProtected, bool includeInherited,
        string? origin, IReadOnlyDictionary<string, string> mods,
        List<ResolvedElement> result, HashSet<string> seen, HashSet<string> visited, int depth)
    {
        if (depth > MaxDepth || !visited.Add(node.Id))
            return;

        var tree = node.Definition.EnsureParsed();
        if (tree is null)
            return;

        var iface = ClassInterfaceExtractor.Extract(tree);
        var imports = iface.Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)
            .ToList();
        var inherited = origin is not null;

        foreach (var e in iface.Elements)
        {
            switch (e.Kind)
            {
                case ClassElementKind.Import:
                case ClassElementKind.Extends:
                    // Imports and extends belong to the queried class only.
                    if (!inherited && seen.Add($"{e.Kind}|{e.Name}"))
                        result.Add(new ResolvedElement(e, null, node.Id, imports));
                    break;

                default: // Component or nested Class
                    if (!e.IsPublic && !includeProtected)
                        break;
                    if (!seen.Add($"{e.Kind}|{e.Name}")) // derived (added first) shadows inherited
                        break;
                    // A modification from a more-derived extends clause overrides this inherited default.
                    var element = e.Kind == ClassElementKind.Component && mods.TryGetValue(e.Name, out var v)
                        ? e with { DefaultValue = v }
                        : e;
                    result.Add(new ResolvedElement(element, origin, node.Id, imports));
                    break;
            }
        }

        if (!includeInherited)
            return;

        foreach (var ext in iface.Elements.Where(e => e.Kind == ClassElementKind.Extends))
        {
            var baseNode = TypeResolver.Resolve(libraries, node.Id, ext.Type, imports);
            if (baseNode is not null)
                Walk(libraries, baseNode, includeProtected, includeInherited, baseNode.Id,
                    MergeMods(ext.Modifications, mods), result, seen, visited, depth + 1);
        }
    }

    // Modifications applying to a base's members: this extends clause's, with any already-accumulated
    // (more-derived) modification winning on a key clash.
    private static IReadOnlyDictionary<string, string> MergeMods(
        IReadOnlyDictionary<string, string>? baseMods, IReadOnlyDictionary<string, string> moreDerived)
    {
        if (baseMods is null || baseMods.Count == 0)
            return moreDerived;
        if (moreDerived.Count == 0)
            return baseMods;
        var merged = new Dictionary<string, string>(baseMods, StringComparer.Ordinal);
        foreach (var kv in moreDerived)
            merged[kv.Key] = kv.Value;
        return merged;
    }
}
