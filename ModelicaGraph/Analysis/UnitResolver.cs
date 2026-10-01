using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// What a type's short-class chain fixes about a <see cref="Real"/>-derived quantity: whether it is
/// one at all, and the <c>unit</c>, <c>displayUnit</c> and <c>quantity</c> it carries. Each attribute
/// is taken from the <b>nearest</b> definition along the chain that sets it, so
/// <c>type Torque = Real(unit="N.m", quantity="Torque")</c> followed by
/// <c>type MyTorque = Torque(displayUnit="kN.m")</c> answers all three for <c>MyTorque</c>, and an
/// alias that sets <c>unit</c> again overrides the one below it.
///
/// <para>A value written as a string literal is that string, unquoted and unescaped (<c>"N.m"</c> is
/// <c>N.m</c>). A value that is any other expression — a parameter, a concatenation — is its source
/// text rebuilt from the tokens, because the attribute is still fixed there and a caller asking
/// <see cref="HasUnit"/> must not be told otherwise; a units check reading it will find it is not a
/// unit string and can say so. Every attribute is null when <see cref="IsRealDerived"/> is false.</para>
/// </summary>
public readonly record struct UnitAttributes(
    bool IsRealDerived, string? Unit, string? DisplayUnit, string? Quantity)
{
    /// <summary>Not a Real quantity, or not resolvable: nothing is known.</summary>
    public static readonly UnitAttributes None = new(false, null, null, null);

    /// <summary>A plain <c>Real</c>: Real-derived, with nothing fixed at type level.</summary>
    public static readonly UnitAttributes PlainReal = new(true, null, null, null);

    /// <summary>Whether a unit is fixed anywhere in the type chain.</summary>
    public bool HasUnit => Unit is not null;
}

/// <summary>
/// Determines whether a component's declared type is a <see cref="Real"/>-derived numeric quantity and,
/// if so, what its type chain fixes about it (<see cref="UnitAttributes"/>). This is what makes unit coverage
/// meaningful for real libraries: a variable typed <c>Modelica.Units.SI.Length</c> carries a unit even
/// though it never writes <c>unit=</c> itself, because <c>type Length = Real(unit="m")</c> does — and
/// aliases chain (<c>type Molarity = MolarDensity = Real(unit=…)</c>). Follows the short-class chain to
/// its predefined base, resolving each hop with the shared <see cref="TypeResolver"/>. Depth- and
/// cycle-guarded; results memoised per resolved class id.
/// </summary>
public static class UnitResolver
{
    /// <summary>
    /// For a type <paramref name="typeText"/> as written in class <paramref name="ownerId"/>, returns
    /// whether it is Real-derived and whether its type chain fixes a unit. A plain <c>Real</c> is
    /// Real-derived with no type-level unit (its unit, if any, is written on the component). Non-numeric
    /// and unresolvable types return (false, false). The yes/no view of <see cref="ResolveAttributes"/>,
    /// and shares its cache.
    /// </summary>
    public static (bool IsRealDerived, bool HasUnit) Resolve(
        DirectedGraph graph, string ownerId, string? typeText,
        IReadOnlyList<string>? imports, IDictionary<string, UnitAttributes>? cache = null,
        TypeResolver.AncestorCache? ancestors = null)
    {
        var attributes = ResolveAttributes(graph, ownerId, typeText, imports, cache, ancestors);
        return (attributes.IsRealDerived, attributes.HasUnit);
    }

    /// <summary>
    /// For a type <paramref name="typeText"/> as written in class <paramref name="ownerId"/>, returns
    /// whether it is Real-derived and the <c>unit</c>, <c>displayUnit</c> and <c>quantity</c> its type
    /// chain fixes, the nearest definition winning for each. A plain <c>Real</c> is
    /// <see cref="UnitAttributes.PlainReal"/>; non-numeric types, connectors and unresolvable types are
    /// <see cref="UnitAttributes.None"/>. <paramref name="cache"/> is keyed by the id of the class a type
    /// resolves to.
    /// </summary>
    public static UnitAttributes ResolveAttributes(
        DirectedGraph graph, string ownerId, string? typeText,
        IReadOnlyList<string>? imports, IDictionary<string, UnitAttributes>? cache = null,
        TypeResolver.AncestorCache? ancestors = null)
    {
        var name = (typeText ?? string.Empty).TrimStart('.').Trim();
        if (name.Length == 0)
            return UnitAttributes.None;
        if (name == "Real")
            return UnitAttributes.PlainReal;
        if (TypeResolver.IsPredefined(name))
            return UnitAttributes.None;   // Integer/Boolean/String/Complex/Clock — not a Real quantity

        var node = TypeResolver.ResolveWithInheritance(graph, ownerId, typeText, imports, ancestors);
        return node is null
            ? UnitAttributes.None
            : ResolveNode(graph, node, cache, new HashSet<string>(StringComparer.Ordinal), 0, ancestors);
    }

    private static UnitAttributes ResolveNode(
        DirectedGraph graph, ModelNode node, IDictionary<string, UnitAttributes>? cache, HashSet<string> visited,
        int depth, TypeResolver.AncestorCache? ancestors)
    {
        if (cache is not null && cache.TryGetValue(node.Id, out var cached))
            return cached;
        if (depth > 32 || !visited.Add(node.Id))
            return UnitAttributes.None;

        // A connector (e.g. RealInput = input Real) is a signal interface, not a physical scalar that
        // should carry a unit — exclude it even though it is technically Real-derived.
        if (node.ClassType == "connector")
        {
            if (cache is not null)
                cache[node.Id] = UnitAttributes.None;
            return UnitAttributes.None;
        }

        var result = UnitAttributes.None;
        // Borrowed: the answer is what gets cached, not the tree, so handing it back costs nothing —
        // a type already resolved is never re-parsed. Every class reached here is a type alias
        // somewhere up a chain, not the class being checked. See ModelDefinition.Borrow.
        if (node.Definition.Borrow<(string? Base, UnitAttributes Own)?>(ShortClassBase) is { } alias)
        {
            var baseName = (alias.Base ?? string.Empty).TrimStart('.').Trim();
            if (baseName == "Real")
            {
                result = alias.Own with { IsRealDerived = true };
            }
            else if (baseName.Length > 0 && !TypeResolver.IsPredefined(baseName))
            {
                // A named base (e.g. another SI type) — resolve it in this alias's own scope and chain.
                var baseNode = TypeResolver.ResolveWithInheritance(graph, node.Id, alias.Base, imports: null, ancestors);
                if (baseNode is not null)
                {
                    var inherited = ResolveNode(graph, baseNode, cache, visited, depth + 1, ancestors);
                    if (inherited.IsRealDerived)
                    {
                        // The nearest definition wins, attribute by attribute.
                        result = new UnitAttributes(
                            true,
                            alias.Own.Unit ?? inherited.Unit,
                            alias.Own.DisplayUnit ?? inherited.DisplayUnit,
                            alias.Own.Quantity ?? inherited.Quantity);
                    }
                }
            }
            // A predefined non-Real base leaves result = None.
        }

        if (cache is not null)
            cache[node.Id] = result;
        return result;
    }

    // For a short class definition `type X = Base(mods)`, returns Base and the attributes mods sets
    // (IsRealDerived left false: that depends on Base). Null when the class is not a short class alias
    // (a long class, enumeration, or der class).
    private static (string? Base, UnitAttributes Own)? ShortClassBase(modelicaParser.Stored_definitionContext tree)
    {
        var classDefs = tree.class_definition();
        if (classDefs is null || classDefs.Length == 0)
            return null;

        var shortSpec = classDefs[0].class_specifier()?.short_class_specifier();
        var typeSpec = shortSpec?.type_specifier();   // null for the enumeration form
        if (typeSpec is null)
            return null;

        return (typeSpec.GetText(), ModifierAttributes(shortSpec!.class_modification()));
    }

    private static UnitAttributes ModifierAttributes(modelicaParser.Class_modificationContext? modification)
    {
        var args = modification?.argument_list();
        if (args is null)
            return UnitAttributes.None;

        string? unit = null, displayUnit = null, quantity = null;
        foreach (var arg in args.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            switch (elemMod?.name()?.GetText())
            {
                case "unit": unit = ValueOf(elemMod.modification()); break;
                case "displayUnit": displayUnit = ValueOf(elemMod.modification()); break;
                case "quantity": quantity = ValueOf(elemMod.modification()); break;
            }
        }
        return new UnitAttributes(false, unit, displayUnit, quantity);
    }

    // The value a modification binds: a string literal's contents, or any other expression's source
    // text. Never GetText(), which drops the spaces between tokens. A modification with no binding
    // (`unit(...)`) or `break` still fixes nothing, but is written, so it reads as an empty string
    // rather than as absent — which keeps HasUnit what it always was: whether `unit` is modified.
    private static string ValueOf(modelicaParser.ModificationContext? modification)
    {
        var expression = modification?.modification_expression();
        if (expression?.Start is null || expression.Stop is null)
            return string.Empty;

        if (expression.Start == expression.Stop && expression.Start.Type == modelicaParser.STRING)
            return Unescape(expression.Start.Text);

        var text = new StringBuilder();
        IToken? previous = null;
        AppendTokens(expression, text, ref previous);
        return text.ToString();
    }

    private static void AppendTokens(IParseTree node, StringBuilder text, ref IToken? previous)
    {
        if (node is ITerminalNode terminal)
        {
            var token = terminal.Symbol;
            if (token.Type is TokenConstants.EOF or modelicaParser.COMMENT or modelicaParser.LINE_COMMENT)
                return;
            if (previous is not null && token.StartIndex > previous.StopIndex + 1)
                text.Append(' ');
            text.Append(token.Text);
            previous = token;
            return;
        }

        for (var i = 0; i < node.ChildCount; i++)
            AppendTokens(node.GetChild(i), text, ref previous);
    }

    // A Modelica string literal's contents: the quotes removed and each escape (\" \\ \n …) undone.
    private static string Unescape(string literal)
    {
        var body = literal.Length >= 2 ? literal[1..^1] : literal;
        if (!body.Contains('\\'))
            return body;

        var text = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\' || i == body.Length - 1)
            {
                text.Append(body[i]);
                continue;
            }
            var next = body[++i];
            text.Append(next switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                'a' => '\a',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                _ => next,   // \" \' \\ \?
            });
        }
        return text.ToString();
    }
}
