using Antlr4.Runtime.Misc;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Flags a numeric quantity that declares no <c>unit</c> attribute and whose type does not fix one
/// either — the author should add a <c>unit</c>, or use a type (e.g. <c>Modelica.Units.SI.Length</c>)
/// that carries one. A presence check only, never dimensional analysis.
///
/// <para>The built-in <c>Real</c> is always judged, because the declaration is the only place its
/// unit could be. Any other type needs resolving — <c>Modelica.Units.SI.Length</c> fixes a unit,
/// <c>type Fraction = Real</c> does not, and only the dependency graph knows which is which — so a
/// caller with a graph passes <c>unitLookup</c>. Without one, only plain <c>Real</c> is judged, which
/// is what a snippet check can honestly say.</para>
///
/// <para>That lookup is why this rule and the Unit coverage dimension now agree. The dimension
/// counted every Real-derived quantity and called an unresolved alias a gap; the rule reported only
/// plain <c>Real</c>. The dashboard therefore showed debt that no finding led anyone to.</para>
/// </summary>
public class MissingUnits : VisitorWithModelNameTracking
{
    private readonly Func<string, string, (bool IsRealDerived, bool TypeHasUnit)>? _unitLookup;
    private string? _typeName;

    /// <param name="unitLookup">Given the class being checked and a declared type name, says whether
    /// that type is a Real-derived quantity and whether its type chain fixes a unit. Null when the
    /// caller has no graph to resolve types with.</param>
    public MissingUnits(
        string basePackage = "",
        Func<string, string, (bool IsRealDerived, bool TypeHasUnit)>? unitLookup = null)
        : base(basePackage)
    {
        _unitLookup = unitLookup;
    }

    public override object? VisitComponent_clause([NotNull] modelicaParser.Component_clauseContext context)
    {
        _typeName = context.type_specifier()?.GetText();
        return base.VisitComponent_clause(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        if (NeedsAUnit(out var typeName) && context.declaration() is { } declaration)
        {
            var name = declaration.IDENT()?.GetText();
            if (name is not null && !HasUnitAttribute(declaration))
                AddFinding(context.Start.Line, $"{typeName} {StripQuotes(name)} does not declare a unit",
                    RuleIds.MissingUnit, StripQuotes(name));
        }
        return base.VisitComponent_declaration(context);
    }

    /// <summary>
    /// Whether the component's type leaves the unit to the declaration: plain <c>Real</c> always, and
    /// any other Real-derived type whose chain fixes no unit — which only a resolver can tell us.
    /// </summary>
    private bool NeedsAUnit(out string typeName)
    {
        typeName = (_typeName ?? string.Empty).TrimStart('.');

        if (typeName.Length == 0)
            return false;
        if (typeName == "Real")
            return true;
        if (_unitLookup is null)
            return false;

        var (isRealDerived, typeHasUnit) = _unitLookup(CurrentModelName, typeName);
        return isRealDerived && !typeHasUnit;
    }

    // True if the declaration's modification carries a `unit` attribute, e.g. Real x(unit="m") = 1.
    private static bool HasUnitAttribute(modelicaParser.DeclarationContext declaration)
    {
        var args = declaration.modification()?.class_modification()?.argument_list();
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

    private static string StripQuotes(string s)
        => s.Length >= 2 && s[0] == '\'' && s[^1] == '\'' ? s[1..^1] : s;
}
