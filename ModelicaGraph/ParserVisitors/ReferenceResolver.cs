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
/// Classes inherited through <c>extends</c> are in scope, before the class's imports and enclosing
/// packages, as Modelica has them. A <c>redeclare</c> is not modelled, so a null result means "not
/// resolvable by these rules", not a guarantee the name is undefined.
/// </summary>
public static class ReferenceResolver
{
    /// <summary>Resolve <paramref name="reference"/> as written in <paramref name="ownerModelId"/>.</summary>
    /// <remarks>
    /// <b>Inherited classes are in scope</b>, where Modelica puts them: before the class's imports and
    /// its enclosing packages. A model extending a base that declares <c>Medium</c> uses that
    /// <c>Medium</c>, and without inheritance its edge went to whatever <c>Medium</c> the package
    /// held, or nowhere.
    /// </remarks>
    /// <param name="ancestors">Each scope's bases, kept for the run; pass one per analysis.</param>
    public static string? Resolve(
        DirectedGraph graph, string ownerModelId, IReadOnlyList<ImportInfo> imports, string reference,
        Analysis.TypeResolver.AncestorCache? ancestors = null)
        => ResolvePath(graph, ownerModelId, imports, reference, ancestors)?.Node.Id;

    /// <summary>
    /// What <paramref name="reference"/> resolves to, saying how: the class each segment names and how
    /// the first was found - an alias, an import, inheritance - which the reference locator records so
    /// a rename and a move know what they may rewrite.
    /// </summary>
    public static Analysis.NameResolution? ResolvePath(
        DirectedGraph graph, string ownerModelId, IReadOnlyList<ImportInfo> imports, string reference,
        Analysis.TypeResolver.AncestorCache? ancestors = null)
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
        return TypeResolver.ResolveNamePath(
            graph, ownerModelId, name, imports.Select(Describe).ToList(), global: reference.StartsWith('.'),
            inherited: true, ancestors);
    }

    /// <summary>An import in the string form <see cref="TypeResolver"/> reads.</summary>
    private static string Describe(ImportInfo import) =>
        !string.IsNullOrEmpty(import.Alias) ? $"{import.Alias} = {import.QualifiedName}"
        : import.IsWildcard ? $"{import.QualifiedName}.*"
        : import.QualifiedName;

    /// <summary>The Modelica built-in types and operators, which are never library classes.</summary>
    public static bool IsBuiltInType(string name) => ModelicaLanguage.IsBuiltInName(name);

    /// <summary>The reference text of a name context (matches ModelAnalyzer).</summary>
    /// <remarks>
    /// <b>With the leading dot of a global name</b>, which the grammar keeps outside <c>name</c>, in
    /// the <c>type_specifier</c> around it (<c>.Modelica.Blocks.Gain</c>). Dropped, the name was
    /// looked up from the class outward and a nearer class of the same name answered for it - or,
    /// inside an encapsulated class, nothing did.
    /// </remarks>
    public static string GetQualifiedName(modelicaParser.NameContext context)
    {
        var text = context.GetText().Trim();
        return context.Parent is modelicaParser.Type_specifierContext specifier
               && specifier.GetChild(0) is Antlr4.Runtime.Tree.ITerminalNode { Symbol.Text: "." }
            ? "." + text
            : text;
    }

    /// <summary>
    /// The name to <b>resolve</b> for a name context: <see cref="GetQualifiedName"/>, and for the name
    /// in an <c>import</c> clause, that as a global name.
    /// </summary>
    /// <remarks>
    /// An import clause's name is always looked up from the top (MLS §13.2.1). Resolved like any other
    /// name, from the class outward, it reached nothing in an <c>encapsulated</c> class - which is
    /// exactly where imports are needed - so the class's imports linked to nothing, and a move left
    /// them naming the class where it used to be. Not for building the class's import list
    /// (<see cref="CollectClassImports"/>): that is the import's own text, which the lookup expands.
    /// </remarks>
    public static string GetReferenceName(modelicaParser.NameContext context)
    {
        var name = GetQualifiedName(context);
        return context.Parent is modelicaParser.Import_clauseContext && !name.StartsWith('.') ? "." + name : name;
    }

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
