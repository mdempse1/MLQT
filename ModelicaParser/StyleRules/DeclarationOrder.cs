using Antlr4.Runtime.Misc;
using ModelicaParser.Helpers;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Checks that declarations within a section are written in the conventional order — inputs and
/// outputs, then constants, then parameters, then variables, then components — which is what
/// <see cref="Visitors.FormattingOptions.DeclarationOrder"/> makes the renderer write.
///
/// <para><b>It extends <see cref="ComponentsBeforeClasses"/> from one boundary to four.</b> That
/// rule enforces the last boundary in the sequence, components before nested classes, by asking
/// <c>class_definition()</c> against <c>component_clause()</c> and nothing else — so the four kinds
/// of declaration could appear in any order among themselves, and the renderer wrote them as one
/// undifferentiated group so the formatter could not have produced a finer order anyway (B252).</para>
///
/// <para><b>The section is the unit, not the class</b>, for the same reason as its sibling: the
/// renderer orders each <c>public</c> and <c>protected</c> section separately and never moves an
/// element across the boundary, so a component in the public section followed by a parameter in the
/// protected one is correctly ordered. A <c>class_definition</c>, <c>import</c> or <c>extends</c>
/// takes no part in this ordering — three other rules have opinions about those — and a class
/// definition does not reset the sequence either, because
/// <see cref="ComponentsBeforeClasses"/> is what reports a declaration that follows one.</para>
///
/// <para>The order is fixed rather than configurable. Some teams write parameters before constants
/// and some the other way, but the formatter has to be able to produce whatever the rule asks for
/// or the finding cannot be cleared, and one order it can always produce is worth more than a
/// setting almost nobody would change.</para>
/// </summary>
public class DeclarationOrder : VisitorWithModelNameTracking
{
    private readonly Func<string, string, bool>? _isSimpleType;

    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause.</param>
    /// <param name="isSimpleType">Given the class being checked and a declared type name, says
    /// whether that type resolves to a simple type rather than to a structured class. Null when the
    /// caller has no graph, leaving only the predefined types recognised as quantities.</param>
    public DeclarationOrder(string basePackage = "", Func<string, string, bool>? isSimpleType = null)
        : base(basePackage)
    {
        _isSimpleType = isSimpleType;
    }

    public override object? VisitComposition([NotNull] modelicaParser.CompositionContext context)
    {
        if (context.children != null)
        {
            foreach (var child in context.children)
            {
                if (child is modelicaParser.Element_listContext elementList)
                    CheckSection(elementList);
            }
        }

        return base.VisitComposition(context);
    }

    /// <summary>
    /// Reports every declaration that belongs to an earlier group than one already written.
    ///
    /// <para>The finding is about the declaration that is out of place and carries its line, and the
    /// message names the kind it should have followed rather than the one it was found after — that
    /// is the instruction, and it stays the same however the rest of the class is rearranged.</para>
    /// </summary>
    private void CheckSection(modelicaParser.Element_listContext elementList)
    {
        var furthestReached = -1;

        foreach (var element in elementList.element())
        {
            if (element.component_clause() is not { } clause)
                continue;

            var kind = DeclarationKinds.KindOf(clause, TypeLookup());
            var position = DeclarationKinds.PositionOf(kind);

            if (position < furthestReached)
            {
                var name = NameOf(clause);
                AddFinding(element.Start.Line,
                    $"{DeclarationKinds.Describe(kind).One} '{name}' is declared after " +
                    $"{DeclarationKinds.Describe(DeclarationKinds.Order[furthestReached]).Many}",
                    RuleIds.DeclarationOrder,
                    elementPath: name);
            }
            else
            {
                furthestReached = position;
            }
        }
    }

    /// <summary>The resolver bound to the class currently being checked, or null when there is none.</summary>
    private Func<string, bool>? TypeLookup()
        => _isSimpleType is null ? null : typeName => _isSimpleType(CurrentModelName, typeName);

    /// <summary>
    /// The first name a component clause declares, for the message and the element path.
    /// A clause can declare several (<c>Real x, y;</c>); the first is enough to find it, and the
    /// whole clause moves as one, so it is one finding rather than one per name.
    /// </summary>
    private static string NameOf(modelicaParser.Component_clauseContext component) =>
        component.component_list()?.component_declaration()?.FirstOrDefault()
            ?.declaration()?.IDENT()?.GetText() ?? "?";
}
