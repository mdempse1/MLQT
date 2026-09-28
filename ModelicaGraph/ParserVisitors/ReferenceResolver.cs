using ModelicaParser;
using ModelicaParser.Helpers;
using ModelicaGraph.Analysis;
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
/// Known limitation (shared with dependency analysis, by design): it does not model names inherited
/// into scope via <c>extends</c>. A null result therefore means "not resolvable by these rules", not a
/// guarantee the name is undefined.
/// </summary>
public static class ReferenceResolver
{
    /// <summary>Resolve <paramref name="reference"/> as written in <paramref name="ownerModelId"/>.</summary>
    public static string? Resolve(
        DirectedGraph graph, string ownerModelId, IReadOnlyList<ImportInfo> imports, string reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return null;

        // A leading dot is Modelica's "from the top": the name is already fully qualified.
        var name = reference.TrimStart('.');
        if (name.Length == 0 || IsBuiltInType(name))
            return null;

        // The lookup is TypeResolver's, so dependency analysis and the analyses that resolve types
        // cannot disagree about a name. This used to be a copy of it that had drifted: a plain
        // `import A.B.C;` made nothing visible here - only an aliased one did, matched by prefix, so
        // `SIx` would have matched an alias `SI` - and neither looked at the imports of enclosing
        // packages, which is where MSL declares `SI` for every block (B292).
        return TypeResolver.ResolveName(graph, ownerModelId, name, imports.Select(Describe).ToList())?.Id;
    }

    /// <summary>An import in the string form <see cref="TypeResolver"/> reads.</summary>
    private static string Describe(ImportInfo import) =>
        !string.IsNullOrEmpty(import.Alias) ? $"{import.Alias} = {import.QualifiedName}"
        : import.IsWildcard ? $"{import.QualifiedName}.*"
        : import.QualifiedName;

    /// <summary>The Modelica built-in types and operators, which are never library classes.</summary>
    public static bool IsBuiltInType(string name) => ModelicaLanguage.IsBuiltInName(name);

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
        else if (context.import_list() is { } list)
            // `import A.B.{C, D};` is `import A.B.C; import A.B.D;`, each visible by its own name.
            foreach (var ident in list.IDENT())
                imports.Add(new ImportInfo { Alias = ident.GetText(), QualifiedName = $"{qualifiedName}.{ident.GetText()}", IsWildcard = false });
        else if (context.GetText().Contains(".*"))
            imports.Add(new ImportInfo { QualifiedName = qualifiedName, IsWildcard = true });
        else
            imports.Add(new ImportInfo { QualifiedName = qualifiedName, IsWildcard = false });
    }
}
