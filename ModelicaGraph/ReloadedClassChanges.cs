using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.Helpers;

namespace ModelicaGraph;

/// <summary>
/// Which classes a reload of whole libraries changed: the ones whose source is different, and the
/// ones below a class whose waivers or imports reach them differently.
///
/// <para>For the reload a VCS operation ends with, which removes a repository's libraries and loads
/// them again from disk. <see cref="EnclosingScopeChanges"/> answers the same question for a reload
/// of <em>some</em> files, by comparing the classes in those files before and after; here every file
/// was reloaded, so the classes to compare are the ones whose source came back different. The VCS
/// pipeline then asked the monitor or the VCS status what changed, and neither can say: the monitor
/// was paused through the operation, and the status names only what is not committed - so after an
/// update over a working copy with local edits, the classes the update itself changed were never
/// re-checked, and a <c>suppress</c> the update brought to a package left every class below it with
/// the findings of the old one (B499).</para>
///
/// <para><b>Nothing is parsed to take the snapshot.</b> A class's source is kept by reference, and
/// its waivers and imports only as far as something has already read them. One nobody read cannot
/// have filtered a finding or resolved a name, so there is nothing stale below it to find.</para>
///
/// <para>Use it in two steps around the reload: <see cref="Capture"/> before the libraries are
/// removed, <see cref="ClassesToReanalyse"/> once they are loaded again.</para>
/// </summary>
public sealed class ReloadedClassChanges
{
    private readonly Dictionary<string, Before> _before;

    private readonly record struct Before(string Code, SuppressionSet? Waivers, IReadOnlyList<string>? Imports);

    private ReloadedClassChanges(Dictionary<string, Before> before) => _before = before;

    /// <summary>
    /// Records the source, and whatever has been read of the waivers and imports, of each of
    /// <paramref name="modelIds"/> that <paramref name="graph"/> holds. Call before they are removed.
    /// </summary>
    public static ReloadedClassChanges Capture(DirectedGraph graph, IEnumerable<string> modelIds)
    {
        var before = new Dictionary<string, Before>(StringComparer.Ordinal);
        foreach (var id in modelIds)
            if (graph.GetNode<ModelNode>(id)?.Definition is { } definition)
                before[id] = new Before(definition.ModelicaCode, definition.Suppressions, definition.Imports);
        return new ReloadedClassChanges(before);
    }

    /// <summary>
    /// Of <paramref name="modelIds"/>, the classes in <paramref name="graph"/> that are new or whose
    /// source differs from what <see cref="Capture"/> recorded, and every class below one of those
    /// whose class-level waivers (<see cref="SuppressionSet.ReachesNestedClassesAs"/>) or imports
    /// differ from what had been read of them. Ordered, each once.
    /// </summary>
    public IReadOnlyList<string> ClassesToReanalyse(DirectedGraph graph, IEnumerable<string> modelIds)
    {
        var present = new List<string>();
        var result = new HashSet<string>(StringComparer.Ordinal);
        var changedScopes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in modelIds)
        {
            if (graph.GetNode<ModelNode>(id)?.Definition is not { } definition)
                continue;
            present.Add(id);

            if (!_before.TryGetValue(id, out var before))
            {
                result.Add(id);
                continue;
            }

            if (string.Equals(before.Code, definition.ModelicaCode, StringComparison.Ordinal))
                continue;

            result.Add(id);
            if (ReachesNestedClassesDifferently(before, definition, id))
                changedScopes.Add(id);
        }

        if (changedScopes.Count > 0)
            foreach (var id in present)
                foreach (var enclosing in ModelicaName.EnclosingNamesOf(id))
                    if (changedScopes.Contains(enclosing))
                    {
                        result.Add(id);
                        break;
                    }

        return [.. result.Order(StringComparer.Ordinal)];
    }

    // Only what was read before is compared: a class whose waivers nobody asked about filtered no
    // finding, and one whose imports nobody asked about resolved no name.
    private static bool ReachesNestedClassesDifferently(Before before, ModelDefinition after, string id) =>
        (before.Waivers is { } waivers && !waivers.ReachesNestedClassesAs(ClassSuppressions.For(after, id)))
        || (before.Imports is { } imports && !imports.SequenceEqual(ClassImports.For(after), StringComparer.Ordinal));
}
