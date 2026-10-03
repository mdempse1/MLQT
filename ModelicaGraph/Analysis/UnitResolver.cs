using System.Text;
using Antlr4.Runtime;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Determines whether a component's declared type is a <see cref="Real"/>-derived numeric quantity and,
/// if so, what its type chain fixes about it (<see cref="UnitAttributes"/>). This is what makes unit coverage
/// meaningful for real libraries: a variable typed <c>Modelica.Units.SI.Length</c> carries a unit even
/// though it never writes <c>unit=</c> itself, because <c>type Length = Real(unit="m")</c> does — and
/// aliases chain (<c>type Molarity = MolarDensity = Real(unit=…)</c>). Follows the chain to its predefined
/// base, each hop read and resolved as every class's bases are (<see cref="ClassElementResolver.Bases"/>,
/// <see cref="ClassElementResolver.ResolveBaseOf"/>) rather than by a reading of its own. A hop is a short class or a
/// <c>type</c> written in the long form around one <c>extends</c> clause, which is how a type that needs
/// an <c>equalityConstraint</c> has to be written (<c>type ReferenceAngle extends SI.Angle; function
/// equalityConstraint ... end ReferenceAngle;</c>). Depth- and cycle-guarded; results memoised per
/// resolved class id.
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
        var isType = node.ClassType == "type";
        if (node.Definition.Borrow<(string Base, UnitAttributes Own)?>(
                tree => AliasOf(ClassInterfaceExtractor.Extract(tree), isType)) is { } alias)
        {
            var baseName = alias.Base.TrimStart('.').Trim();
            if (baseName == "Real")
            {
                result = alias.Own with { IsRealDerived = true };
            }
            else if (baseName.Length > 0 && !TypeResolver.IsPredefined(baseName))
            {
                // A named base (e.g. another SI type), resolved as every base name is - a short class's
                // and an extends clause's alike - and chained.
                var baseNode = ClassElementResolver.ResolveBaseOf(
                    graph, node, alias.Base, ClassImports.For(node.Definition), ancestors);
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

    // The base a type names and the attributes it sets on it (IsRealDerived left false: that depends on
    // the base), from either form: a short class `type X = Base(mods)`, or - for a `type` - the long
    // form `type X extends Base(mods); ... end X;`, which MLS §4.6 allows a type to be and which is the
    // only way to give one an equalityConstraint. Read as every class's bases are read
    // (ClassElementResolver.Bases), so a class extends - `redeclare type extends T` - is one too. Null
    // for anything else: an enumeration, a der class, a type with more than one base, and a long class
    // that is not a type - a model extending a base is no quantity, and following its chain would cost
    // a walk of every component type reached for nothing.
    private static (string Base, UnitAttributes Own)? AliasOf(ClassInterface iface, bool isType)
    {
        if (iface.ShortClassBase is null && !isType)
            return null;

        return ClassElementResolver.Bases(iface).ToList() is [{ Type: { Length: > 0 } written } only]
            ? (written, Attributes(only.Modifications))
            : null;
    }

    // What a modification sets of the three attributes. One that is not a value of it - a bare `unit`,
    // or `unit(...)` with or without a binding after it - is none, as ClassElement.Modifications says,
    // and fixes nothing. Read off the tree, the first two used to be an empty string, which counted the
    // type as having a unit, and the third the binding; an attribute is a String and has no
    // modification of its own, so none of them is valid Modelica, and no library tested writes one.
    private static UnitAttributes Attributes(IReadOnlyDictionary<string, string>? modifications)
        => modifications is null
            ? UnitAttributes.None
            : new UnitAttributes(false, ValueOf(modifications, "unit"), ValueOf(modifications, "displayUnit"),
                ValueOf(modifications, "quantity"));

    private static string? ValueOf(IReadOnlyDictionary<string, string> modifications, string attribute)
        => modifications.TryGetValue(attribute, out var written) ? ValueOf(written) : null;

    // The value a modification binds: a string literal's contents, or any other expression's source
    // text. The interface keeps it as source text, so it is lexed again to be read as tokens: a literal
    // in parentheses is still a literal - `unit=("m")` is `m` - so the pairs wrapping the whole value
    // are removed first; one that does not wrap all of it, as in `("a") + ("b")`, leaves more than one
    // token and is source text.
    private static string ValueOf(string written)
    {
        var lexer = new modelicaLexer(new AntlrInputStream(written));
        lexer.RemoveErrorListeners();
        var tokens = lexer.GetAllTokens()
            .Where(t => t.Channel == TokenConstants.DefaultChannel
                        && t.Type is not (modelicaLexer.COMMENT or modelicaLexer.LINE_COMMENT))
            .ToList();
        if (tokens.Count == 0)
            return written;

        var first = 0;
        var last = tokens.Count - 1;
        while (last - first >= 2 && tokens[first].Text == "(" && tokens[last].Text == ")")
        {
            first++;
            last--;
        }
        if (first == last && tokens[first].Type == modelicaParser.STRING)
            return Unescape(tokens[first].Text);

        var text = new StringBuilder();
        IToken? previous = null;
        foreach (var token in tokens)
        {
            if (previous is not null && token.StartIndex > previous.StopIndex + 1)
                text.Append(' ');
            text.Append(token.Text);
            previous = token;
        }
        return text.ToString();
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
