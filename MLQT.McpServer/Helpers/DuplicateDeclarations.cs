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
        var old = Declared(before);
        var now = Declared(after);

        var twice = now.GroupBy(d => d.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1 && g.Count() > old.Count(d => d.Name == g.Key))
            .Select(g => g.Key)
            .ToList();
        if (twice.Count > 0)
            return $"'{classId}' would declare {Names(twice)} twice";

        var added = now.Where(d => !d.Redeclare && !old.Any(o => o.Name == d.Name)).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        if (added.Count == 0 || libraries.GetModelById(classId) is not { } node)
            return null;

        var inherited = ClassElementResolver.Collect(libraries.CombinedGraph, node, includeProtected: true, includeInherited: true)
            .Where(m => m.OwnerId != classId && added.Contains(m.Element.Name)
                        && m.Element.Kind is ClassElementKind.Component or ClassElementKind.Class)
            .Select(m => $"'{m.Element.Name}' (declared in '{m.OwnerId}')")
            .Distinct()
            .ToList();
        return inherited.Count > 0
            ? $"'{classId}' would declare {string.Join(", ", inherited)} again, though it already inherits it"
            : null;
    }

    private static List<(string Name, bool Redeclare)> Declared(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return [];
        var (tree, errors) = ModelicaParserHelper.ParseWithErrors(code);
        if (errors.Count > 0)
            return []; // the parse check that follows refuses it, with a better account of why
        return [.. ClassInterfaceExtractor.Extract(tree).Elements
            .Where(e => e.Kind is ClassElementKind.Component or ClassElementKind.Class)
            .Select(e => (e.Name, e.Prefixes.Contains("redeclare")))];
    }

    private static string Names(IReadOnlyList<string> names) => string.Join(", ", names.Select(n => $"'{n}'"));
}
