using ModelicaGraph.DataTypes;

namespace ModelicaGraph;

/// <summary>
/// The import statements a class declares, read once per class and kept on its
/// <see cref="ModelDefinition"/>.
///
/// <para>Name lookup has to see them from the classes <em>inside</em> it, not only from the class
/// itself. MSL declares <c>import Modelica.Units.SI;</c> once, in <c>Modelica.Blocks</c>, and every
/// block below writes <c>SI.Time</c>; asked only of the class doing the writing, that name resolved to
/// nothing, so <c>LimPID</c> lost the edges to the types it uses (B292). A package is an enclosing
/// scope to hundreds of classes and every one of them asks, so the answer is cached rather than
/// re-parsed.</para>
///
/// <para>In the string form <see cref="Analysis.TypeResolver"/> reads — <c>SI = Modelica.Units.SI</c>,
/// <c>Modelica.Units.SI</c>, <c>Modelica.Units.SI.*</c>, <c>Modelica.Units.SI.{Time, Length}</c> — so
/// there is one interpretation of what an import makes visible.</para>
///
/// <para><b>Read once, however many threads ask at once.</b> The classes nested in a package ask it
/// for its imports from every worker of a parallel pass, and all of them arrive before any has an
/// answer. Unlocked, each parsed the class for itself - and <c>Borrow</c> threw each tree away, so
/// later arrivals parsed it again. For a Dymola FMU import model of 6 MB with 4,478 classes inside it,
/// that was about 280 parses of the model, 88% of a 25-second dependency refresh. So a miss takes a
/// lock on the class and looks again; a hit, which is nearly every call, takes none.
/// <see cref="ModelDefinition.ModelicaCode"/> clears it.</para>
/// </summary>
public static class ClassImports
{
    /// <summary>
    /// The imports <paramref name="definition"/> declares, reading them if nobody has yet. Never null
    /// and never throws: a class that will not parse makes nothing visible.
    /// </summary>
    public static IReadOnlyList<string> For(ModelDefinition definition)
    {
        if (definition.Imports is { } cached)
            return cached;

        // Nothing else locks a definition, and nothing below asks about another class.
        lock (definition)
        {
            return definition.Imports ?? Read(definition);
        }
    }

    private static IReadOnlyList<string> Read(ModelDefinition definition)
    {
        IReadOnlyList<string> found;
        try
        {
            // Borrowed: a package asked about by the classes inside it is usually not parsed at the
            // time, and nothing else wants its tree.
            found = definition.Borrow<IReadOnlyList<string>>(Extract, []);
        }
        catch
        {
            found = [];
        }

        definition.Imports = found.Count == 0 ? [] : found;
        return definition.Imports;
    }

    /// <summary>The imports of the outermost class in <paramref name="tree"/> - its own, not those
    /// of classes nested in it, which are scopes of their own.</summary>
    internal static IReadOnlyList<string> Extract(modelicaParser.Stored_definitionContext tree)
    {
        var composition = tree.class_definition().FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.element_list() is not { } lists)
            return [];

        var imports = new List<string>();
        foreach (var list in lists)
            foreach (var element in list.element())
                if (element.import_clause() is { } clause && Describe(clause) is { } import)
                    imports.Add(import);
        return imports;
    }

    /// <summary>One import clause in <see cref="Analysis.TypeResolver"/>'s string form.</summary>
    public static string? Describe(modelicaParser.Import_clauseContext clause)
    {
        var name = clause.name()?.GetText();
        if (string.IsNullOrEmpty(name))
            return null;

        if (clause.IDENT() is { } alias)
            return $"{alias.GetText()} = {name}";
        if (clause.import_list() is { } list)
            return $"{name}.{{{string.Join(", ", list.IDENT().Select(i => i.GetText()))}}}";
        if (clause.children.Any(child => child.GetText() == ".*"))
            return $"{name}.*";
        return name;
    }
}
