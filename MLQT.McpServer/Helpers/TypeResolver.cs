using ModelicaGraph.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Best-effort resolution of a type name written inside a class to the loaded <see cref="ModelNode"/>
/// it refers to. Tries an exact match, then the class's own imports, then a relative lookup up the
/// package hierarchy — mirroring how the dependency analyzer resolves references. It intentionally does
/// NOT model names inherited through <c>extends</c> (a documented limitation shared with dependency
/// analysis), so an unresolved result is "not found by these rules", not a guarantee the type is
/// undefined. Phase 2 will unify this with a shared, span-aware resolver.
/// </summary>
internal static class TypeResolver
{
    private static readonly HashSet<string> PredefinedTypes = new(StringComparer.Ordinal)
    {
        "Real", "Integer", "Boolean", "String", "Complex", "Clock"
    };

    /// <summary>True for the Modelica built-in/predefined types, which are never library classes.</summary>
    public static bool IsPredefined(string? typeName)
        => typeName is not null && PredefinedTypes.Contains(typeName.TrimStart('.').Trim());

    /// <summary>
    /// Resolve <paramref name="typeText"/> (as written in class <paramref name="ownerId"/>) to a loaded
    /// class, or null if these rules cannot resolve it. <paramref name="imports"/> are the class's
    /// import statements (from the interface extractor), used to expand aliases/wildcards.
    /// </summary>
    public static ModelNode? Resolve(
        ILibraryDataService libraries, string ownerId, string? typeText, IReadOnlyList<string>? imports = null)
    {
        if (string.IsNullOrWhiteSpace(typeText))
            return null;

        var name = typeText.TrimStart('.').Trim();
        if (name.Length == 0 || IsPredefined(name))
            return null;

        // 1. Already fully-qualified.
        if (libraries.GetModelById(name) is { } exact)
            return exact;

        // 2. Via the class's own imports.
        if (imports is not null)
            foreach (var import in imports)
                if (ResolveViaImport(libraries, import, name) is { } viaImport)
                    return viaImport;

        // 3. Relative: start in the class's own scope and walk outward through enclosing packages.
        var parts = ownerId.Split('.');
        for (var take = parts.Length; take >= 0; take--)
        {
            var prefix = string.Join('.', parts.Take(take));
            var candidate = prefix.Length == 0 ? name : $"{prefix}.{name}";
            if (libraries.GetModelById(candidate) is { } node)
                return node;
        }

        return null;
    }

    private static ModelNode? ResolveViaImport(ILibraryDataService libraries, string import, string name)
    {
        var stmt = import.Trim();

        // Alias: "SI = Modelica.Units.SI"
        var eq = stmt.IndexOf('=');
        if (eq >= 0)
        {
            var alias = stmt[..eq].Trim();
            var target = stmt[(eq + 1)..].Trim();
            if (name == alias)
                return libraries.GetModelById(target);
            if (name.StartsWith(alias + ".", StringComparison.Ordinal))
                return libraries.GetModelById(target + name[alias.Length..]);
            return null;
        }

        // Wildcard: "Modelica.Units.SI.*"
        if (stmt.EndsWith(".*", StringComparison.Ordinal))
            return libraries.GetModelById($"{stmt[..^2]}.{name}");

        // Explicit list: "Modelica.Units.SI.{Voltage, Current}"
        var listIdx = stmt.IndexOf(".{", StringComparison.Ordinal);
        if (listIdx >= 0)
            return libraries.GetModelById($"{stmt[..listIdx]}.{name}");

        // Plain: "Modelica.Units.SI" — the last segment becomes the implicit alias.
        var lastDot = stmt.LastIndexOf('.');
        var leaf = lastDot >= 0 ? stmt[(lastDot + 1)..] : stmt;
        if (name == leaf)
            return libraries.GetModelById(stmt);
        if (name.StartsWith(leaf + ".", StringComparison.Ordinal))
            return libraries.GetModelById(stmt + name[leaf.Length..]);
        return null;
    }
}
