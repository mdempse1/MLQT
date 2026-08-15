using Antlr4.Runtime.Misc;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Flags a plain <c>Real</c> variable/parameter that declares no <c>unit</c> attribute — the author
/// should either add a <c>unit</c> or use an SI type (e.g. <c>Modelica.Units.SI.Length</c>) that
/// carries one. This is a presence check only, never dimensional analysis.
/// <para>
/// Only the built-in <c>Real</c> is flagged: a component already typed with an SI/derived type is
/// left alone (that type supplies the unit), so there are no false positives from types we would
/// otherwise have to resolve. A user type that aliases <c>Real</c> without a unit is not yet caught —
/// that needs type resolution and is a later increment.
/// </para>
/// </summary>
public class MissingUnits : VisitorWithModelNameTracking
{
    private bool _isReal;

    public MissingUnits(string basePackage = "") : base(basePackage) { }

    public override object? VisitComponent_clause([NotNull] modelicaParser.Component_clauseContext context)
    {
        _isReal = context.type_specifier()?.GetText() == "Real";
        return base.VisitComponent_clause(context);
    }

    public override object? VisitComponent_declaration([NotNull] modelicaParser.Component_declarationContext context)
    {
        if (_isReal && context.declaration() is { } declaration)
        {
            var name = declaration.IDENT()?.GetText();
            if (name is not null && !HasUnitAttribute(declaration))
                AddViolation(context.Start.Line, $"Real {StripQuotes(name)} does not declare a unit",
                    RuleIds.MissingUnit, StripQuotes(name));
        }
        return base.VisitComponent_declaration(context);
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
