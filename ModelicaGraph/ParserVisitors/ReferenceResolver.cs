using ModelicaParser;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>A collected import statement used during reference resolution.</summary>
public sealed class ImportInfo
{
    public string? Alias { get; set; }
    public required string QualifiedName { get; set; }
    public bool IsWildcard { get; set; }
}

/// <summary>
/// Shared Modelica name resolution: turns a (possibly simple, relative or aliased) type/component
/// reference written inside a class into the fully-qualified id of the loaded <see cref="ModelNode"/>
/// it refers to, or null. Extracted from <see cref="ModelAnalyzer"/> so dependency analysis and the
/// reference-locating used by rename resolve by the SAME rules.
///
/// Known limitations (shared with dependency analysis, by design): it does not model names inherited
/// into scope via <c>extends</c>, and alias expansion is a substring replace. A null result therefore
/// means "not resolvable by these rules", not a guarantee the name is undefined.
/// </summary>
public static class ReferenceResolver
{
    /// <summary>Resolve <paramref name="reference"/> as written in <paramref name="ownerModelId"/>.</summary>
    public static string? Resolve(
        DirectedGraph graph, string ownerModelId, IReadOnlyList<ImportInfo> imports, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || IsBuiltInType(reference))
            return null;

        // Already fully-qualified.
        if (graph.GetNode<ModelNode>(reference) != null)
            return reference;

        // Import aliases.
        foreach (var import in imports)
        {
            if (!string.IsNullOrEmpty(import.Alias) && reference.StartsWith(import.Alias))
            {
                var resolved = reference.Replace(import.Alias, import.QualifiedName);
                if (graph.GetNode<ModelNode>(resolved) != null)
                    return resolved;
            }
        }

        // Wildcard imports.
        foreach (var import in imports)
        {
            if (!import.IsWildcard)
                continue;
            var candidate = $"{import.QualifiedName}.{reference}";
            if (graph.GetNode<ModelNode>(candidate) != null)
                return candidate;
        }

        // Relative lookup up the owner's package hierarchy.
        if (ownerModelId.Contains('.'))
        {
            var parts = ownerModelId.Split('.');
            for (var i = parts.Length - 1; i >= 1; i--)
            {
                var packagePath = string.Join(".", parts.Take(i));
                var candidate = $"{packagePath}.{reference}";
                if (graph.GetNode<ModelNode>(candidate) != null)
                    return candidate;
            }
        }

        return null;
    }

    /// <summary>The Modelica built-in types and operators, which are never library classes.</summary>
    public static bool IsBuiltInType(string name)
        => BuiltInTypes.Contains(name.Split('.')[0]);

    private static readonly HashSet<string> BuiltInTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Real", "Integer", "Boolean", "String",
        "StateSelect", "AssertionLevel",
        "time", "der", "pre", "edge", "change", "reinit",
        "sample", "initial", "terminal", "noEvent",
        "smooth", "terminate", "abs", "sign", "sqrt",
        "sin", "cos", "tan", "asin", "acos", "atan", "atan2",
        "sinh", "cosh", "tanh", "exp", "log", "log10",
        "min", "max", "sum", "product"
    };

    /// <summary>The reference text of a name context (matches ModelAnalyzer).</summary>
    public static string GetQualifiedName(modelicaParser.NameContext context) => context.GetText().Trim();

    /// <summary>The reference text of a component reference, minus any call arguments.</summary>
    public static string GetComponentReferenceName(modelicaParser.Component_referenceContext context)
        => context.GetText().Split('(')[0].Trim();

    /// <summary>
    /// Collect a class's own import statements (its scope), the way ModelAnalyzer does. Used by the
    /// reference locator, which needs each class's imports without relying on visit order.
    /// </summary>
    public static List<ImportInfo> CollectClassImports(modelicaParser.Class_definitionContext cls)
    {
        var imports = new List<ImportInfo>();
        var composition = cls.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.element_list() is not { } lists)
            return imports;

        foreach (var list in lists)
            foreach (var element in list.element())
                if (element.import_clause() is { } import)
                    AddImport(imports, import);
        return imports;
    }

    /// <summary>Append one import clause to <paramref name="imports"/> (matches ModelAnalyzer's logic).</summary>
    public static void AddImport(List<ImportInfo> imports, modelicaParser.Import_clauseContext context)
    {
        var name = context.name();
        if (name == null)
            return;

        var qualifiedName = GetQualifiedName(name);
        if (context.IDENT() != null)
            imports.Add(new ImportInfo { Alias = context.IDENT().GetText(), QualifiedName = qualifiedName, IsWildcard = false });
        else if (context.GetText().Contains(".*"))
            imports.Add(new ImportInfo { QualifiedName = qualifiedName, IsWildcard = true });
        else
            imports.Add(new ImportInfo { QualifiedName = qualifiedName, IsWildcard = false });
    }
}
