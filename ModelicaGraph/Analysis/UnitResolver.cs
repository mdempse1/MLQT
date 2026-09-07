using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Determines whether a component's declared type is a <see cref="Real"/>-derived numeric quantity and,
/// if so, whether a <c>unit</c> is fixed anywhere in its type chain. This is what makes unit coverage
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
    /// and unresolvable types return (false, false).
    /// </summary>
    public static (bool IsRealDerived, bool HasUnit) Resolve(
        DirectedGraph graph, string ownerId, string? typeText,
        IReadOnlyList<string>? imports, IDictionary<string, (bool, bool)>? cache = null)
    {
        var name = (typeText ?? string.Empty).TrimStart('.').Trim();
        if (name.Length == 0)
            return (false, false);
        if (name == "Real")
            return (true, false);
        if (TypeResolver.IsPredefined(name))
            return (false, false);   // Integer/Boolean/String/Complex/Clock — not a Real quantity

        var node = TypeResolver.ResolveWithInheritance(graph, ownerId, typeText, imports);
        return node is null ? (false, false) : ResolveNode(graph, node, cache, new HashSet<string>(StringComparer.Ordinal), 0);
    }

    private static (bool, bool) ResolveNode(
        DirectedGraph graph, ModelNode node, IDictionary<string, (bool, bool)>? cache, HashSet<string> visited, int depth)
    {
        if (cache is not null && cache.TryGetValue(node.Id, out var cached))
            return cached;
        if (depth > 32 || !visited.Add(node.Id))
            return (false, false);

        // A connector (e.g. RealInput = input Real) is a signal interface, not a physical scalar that
        // should carry a unit — exclude it even though it is technically Real-derived.
        if (node.ClassType == "connector")
        {
            if (cache is not null)
                cache[node.Id] = (false, false);
            return (false, false);
        }

        var result = (false, false);
        // Borrowed: the answer is what gets cached, not the tree, so handing it back costs nothing —
        // a type already resolved is never re-parsed. Every class reached here is a type alias
        // somewhere up a chain, not the class being checked. See ModelDefinition.Borrow.
        if (node.Definition.Borrow<(string? Base, bool HasUnit)?>(ShortClassBase) is { } info)
        {
            var (baseType, hasUnit) = info;
            var baseName = (baseType ?? string.Empty).TrimStart('.').Trim();
            if (baseName == "Real")
            {
                result = (true, hasUnit);
            }
            else if (baseName.Length > 0 && !TypeResolver.IsPredefined(baseName))
            {
                // A named base (e.g. another SI type) — resolve it in this alias's own scope and chain.
                var baseNode = TypeResolver.ResolveWithInheritance(graph, node.Id, baseType, imports: null);
                if (baseNode is not null)
                {
                    var (baseIsReal, baseHasUnit) = ResolveNode(graph, baseNode, cache, visited, depth + 1);
                    result = (baseIsReal, baseIsReal && (hasUnit || baseHasUnit));
                }
            }
            // A predefined non-Real base leaves result = (false, false).
        }

        if (cache is not null)
            cache[node.Id] = result;
        return result;
    }

    // For a short class definition `type X = Base(mods)`, returns (Base, whether mods fixes a unit).
    // Null when the class is not a short class alias (a long class, enumeration, or der class).
    private static (string? Base, bool HasUnit)? ShortClassBase(modelicaParser.Stored_definitionContext tree)
    {
        var classDefs = tree.class_definition();
        if (classDefs is null || classDefs.Length == 0)
            return null;

        var shortSpec = classDefs[0].class_specifier()?.short_class_specifier();
        var typeSpec = shortSpec?.type_specifier();   // null for the enumeration form
        if (typeSpec is null)
            return null;

        return (typeSpec.GetText(), ModifierHasUnit(shortSpec!.class_modification()));
    }

    private static bool ModifierHasUnit(modelicaParser.Class_modificationContext? modification)
    {
        var args = modification?.argument_list();
        if (args is null)
            return false;
        foreach (var arg in args.argument())
        {
            var elemMod = arg.element_modification_or_replaceable()?.element_modification();
            if (elemMod?.name()?.GetText() == "unit")
                return true;
        }
        return false;
    }
}
