using ModelicaGraph.Analysis;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Whether an edit declares a name twice in one class: two components (or a component and a nested
/// class) of one name, or a name a base class already declares. Both parse, so the parse check every
/// write makes does not see them, and an edit that made one - a replace that renamed a parameter to a
/// name already taken, <c>rearLoad</c> - was found only when the model failed to build.
/// </summary>
/// <remarks>
/// Only what the edit <b>introduces</b> is refused: a class that already declared a name twice can still
/// be edited, or nothing could be done to fix it. An element written with <c>redeclare</c> replaces the
/// inherited one of its name, and is not a second declaration.
/// </remarks>
internal static class DuplicateDeclarations
{
    /// <summary>What the edit from <paramref name="before"/> to <paramref name="after"/> declares twice,
    /// as a sentence for the refusal, or null when it declares nothing twice.</summary>
    public static string? Introduced(ILibraryDataService libraries, string classId, string before, string after)
    {
        // Each text parsed once: this runs on every class edit.
        var oldElements = Elements(before);
        var newElements = Elements(after);
        var old = Declared(oldElements);
        var now = Declared(newElements);

        var twice = now.GroupBy(d => d.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1 && g.Count() > old.Count(d => d.Name == g.Key))
            .Select(g => g.Key)
            .ToList();
        if (twice.Count > 0)
            return $"'{classId}' would declare {Names(twice)} twice";

        // Against the bases each text extends, not the class's as loaded: replacing `extends Base;` with a
        // declaration Base had was refused as "already inherits", and an added extends clause bringing a
        // name the class declares was not seen at all.
        var wasInherited = Clashes(libraries, classId, old, Bases(oldElements));
        var clashes = Clashes(libraries, classId, now, Bases(newElements))
            .Where(c => !wasInherited.Contains(c))
            .ToList();
        return clashes.Count > 0
            ? $"'{classId}' would declare {string.Join(", ", clashes)}, which it also inherits"
            : null;
    }

    /// <summary>Each name declared (without redeclare) that one of <paramref name="bases"/> also gives the class.</summary>
    private static HashSet<string> Clashes(
        ILibraryDataService libraries, string classId, List<(string Name, bool Redeclare)> declared, List<string> bases)
    {
        var names = declared.Where(d => !d.Redeclare).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        if (names.Count == 0)
            return found;
        var graph = libraries.CombinedGraph;
        foreach (var baseName in bases)
        {
            if (TypeResolver.Resolve(graph, classId, baseName, null) is not { } baseNode)
                continue;
            foreach (var m in ClassElementResolver.Collect(graph, baseNode, includeProtected: true, includeInherited: true))
                if (names.Contains(m.Element.Name) && m.Element.Kind is ClassElementKind.Component or ClassElementKind.Class)
                    found.Add($"'{m.Element.Name}' (declared in '{m.OwnerId}')");
        }
        return found;
    }

    private static List<(string Name, bool Redeclare)> Declared(IReadOnlyList<ClassElement> elements) =>
        [.. elements.Where(e => e.Kind is ClassElementKind.Component or ClassElementKind.Class)
            .Select(e => (e.Name, e.Prefixes.Contains("redeclare")))];

    private static List<string> Bases(IReadOnlyList<ClassElement> elements) =>
        [.. elements.Where(e => e.Kind == ClassElementKind.Extends).Select(e => e.Name)];

    private static IReadOnlyList<ClassElement> Elements(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return [];
        var (tree, errors) = ModelicaParserHelper.ParseWithErrors(code);
        return errors.Count > 0
            ? [] // the parse check that follows refuses it, with a better account of why
            : ClassInterfaceExtractor.Extract(tree).Elements;
    }

    private static string Names(IReadOnlyList<string> names) => string.Join(", ", names.Select(n => $"'{n}'"));
}
