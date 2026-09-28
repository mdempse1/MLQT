using ModelicaParser;

namespace ModelicaParser.Helpers;

/// <summary>
/// What kind of thing a declaration declares, for the one ordering convention that spans all of
/// them. In the order they are written.
/// </summary>
public enum DeclarationKind
{
    /// <summary>
    /// A declaration carrying <c>input</c> or <c>output</c>. First, and as one group rather than two:
    /// a function's signature reads as a signature, and a connector conventionally keeps its
    /// causal members together.
    /// </summary>
    InputOutput,

    /// <summary>A <c>constant</c>.</summary>
    Constant,

    /// <summary>A <c>parameter</c>.</summary>
    Parameter,

    /// <summary>
    /// A quantity — something whose declared type is a simple type. <c>Real x</c> is one and so is
    /// <c>SI.Length x</c>, which is the whole difficulty: by the grammar its type is a class.
    /// </summary>
    Variable,

    /// <summary>An instance of a structured class — <c>Resistor r</c>.</summary>
    Component
}

/// <summary>
/// <b>The only answer to "what kind of declaration is this?"</b> — asked by
/// <c>MLQT.Style.DeclarationOrder</c>, which reports declarations out of order, and by
/// <c>ModelicaRenderer</c>, which writes them in it. They have to give the same answer or the rule
/// reports an arrangement the formatter cannot produce, which is a finding nobody can clear.
///
/// <para>Three of the five kinds are syntax: <c>input</c>, <c>output</c>, <c>constant</c> and
/// <c>parameter</c> are all in the <c>type_prefix</c>. <b>Telling a variable from a component is
/// not.</b> <c>Real x</c> is a variable and <c>Resistor r</c> is a component, but <c>SI.Length x</c>
/// is a variable by every convention while its type is a class by the grammar — so the declared type
/// has to be resolved, which needs the dependency graph. A caller with one passes
/// <paramref name="isSimpleType"/>; without it only the predefined types are recognised, which is
/// what a check with no graph behind it can honestly say. <c>MissingUnits</c> is the precedent and
/// the shape is deliberately the same.</para>
/// </summary>
public static class DeclarationKinds
{
    /// <summary>The order the kinds are written in, which is the order of the enum.</summary>
    public static readonly IReadOnlyList<DeclarationKind> Order = new[]
    {
        DeclarationKind.InputOutput,
        DeclarationKind.Constant,
        DeclarationKind.Parameter,
        DeclarationKind.Variable,
        DeclarationKind.Component
    };

    /// <summary>
    /// Where a kind comes in <see cref="Order"/>. Derived from the list rather than cast from the
    /// enum so that reordering the list is the one edit that changes the convention.
    /// </summary>
    public static int PositionOf(DeclarationKind kind) => _positions[(int)kind];

    private static readonly int[] _positions = BuildPositions();

    private static int[] BuildPositions()
    {
        var positions = new int[Enum.GetValues<DeclarationKind>().Length];
        for (var i = 0; i < Order.Count; i++)
            positions[(int)Order[i]] = i;
        return positions;
    }

    /// <summary>What to call a kind in a message, in the singular and in the plural.</summary>
    public static (string One, string Many) Describe(DeclarationKind kind) => kind switch
    {
        DeclarationKind.InputOutput => ("Input/output", "inputs and outputs"),
        DeclarationKind.Constant => ("Constant", "constants"),
        DeclarationKind.Parameter => ("Parameter", "parameters"),
        DeclarationKind.Variable => ("Variable", "variables"),
        _ => ("Component", "components")
    };

    /// <summary>
    /// Whether the class owning <paramref name="composition"/> must keep its declarations in source
    /// order whatever the convention says — <b>true for a <c>record</c> and an <c>operator record</c></b>,
    /// and asked by both the rule and the renderer so they cannot disagree about it (B304).
    ///
    /// <para>A record's declaration order is its constructor's signature (Modelica §12.6): the
    /// constructor's inputs are its fields in the order they are declared, so <c>R(2.0)</c> binds the
    /// first. Writing a parameter ahead of the variable above it makes every positional call in the
    /// library set a different field, silently. Nothing else has that property — a function's
    /// signature is inputs and outputs, which are one group; a connector's members are matched by name
    /// in a connection; a model, block or class is never called positionally — so the exemption is the
    /// record's alone.</para>
    ///
    /// <para>It covers the record's <c>extends</c> clauses too, which the renderer and
    /// <c>ExtendsClausesAtTop</c> leave where they stand: Modelica places the inherited fields at the
    /// clause (§7.1), so lifting it above a field reorders the constructor's inputs the same way
    /// (B378).</para>
    /// </summary>
    public static bool KeepsSourceOrder(modelicaParser.CompositionContext composition)
    {
        for (var node = composition.Parent; node is not null; node = node.Parent)
        {
            if (node is modelicaParser.Class_definitionContext definition)
                return definition.class_prefixes()?.children?.Any(c => c.GetText() == "record") == true;
        }

        return false;
    }

    /// <param name="isSimpleType">Given a declared type name as written, says whether it resolves to
    /// a simple type rather than to a structured class. Null when the caller has no graph.</param>
    public static DeclarationKind KindOf(
        modelicaParser.Component_clauseContext clause, Func<string, bool>? isSimpleType = null)
    {
        foreach (var word in PrefixWords(clause.type_prefix()))
        {
            // input/output before constant/parameter, because both can be present: a function may
            // write `parameter input Real n`, and it is still part of the signature.
            if (word is "input" or "output")
                return DeclarationKind.InputOutput;
        }

        foreach (var word in PrefixWords(clause.type_prefix()))
        {
            if (word == "constant")
                return DeclarationKind.Constant;
            if (word == "parameter")
                return DeclarationKind.Parameter;
        }

        return IsQuantity(clause.type_specifier()?.name()?.GetText(), isSimpleType)
            ? DeclarationKind.Variable
            : DeclarationKind.Component;
    }

    /// <summary>
    /// Whether a declared type names a quantity. The predefined types always do, whatever the caller
    /// knows; anything else is the resolver's question, and an unresolved name is a component —
    /// which keeps a class with no graph behind it writing its declarations in source order rather
    /// than sorting them by a guess.
    /// </summary>
    public static bool IsQuantity(string? typeName, Func<string, bool>? isSimpleType = null)
    {
        var name = typeName?.TrimStart('.') ?? string.Empty;
        if (name.Length == 0)
            return false;
        if (ModelicaLanguage.PredefinedTypes.Contains(name))
            return true;
        return isSimpleType?.Invoke(name) == true;
    }

    /// <summary>
    /// The prefix keywords, read as tokens. <c>GetText()</c> runs them together — <c>flow parameter</c>
    /// comes back as <c>flowparameter</c> — so a substring test on it would find <c>parameter</c>
    /// inside a type called <c>parameterised</c> just as readily.
    /// </summary>
    private static IEnumerable<string> PrefixWords(modelicaParser.Type_prefixContext? prefix)
    {
        if (prefix?.children is null)
            yield break;

        foreach (var child in prefix.children)
            yield return child.GetText();
    }
}
