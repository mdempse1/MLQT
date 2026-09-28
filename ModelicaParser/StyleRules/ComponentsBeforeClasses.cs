using Antlr4.Runtime.Misc;
using ModelicaParser.DataTypes;

namespace ModelicaParser.StyleRules;

/// <summary>
/// Visitor that checks component declarations appear before nested class definitions, which is what
/// <see cref="Visitors.FormattingOptions.ComponentsBeforeClasses"/> makes the renderer write.
///
/// <para>This rule exists because the option did not have one. It was the only layout choice MLQT
/// could apply and never report: a repository could switch it on, have every saved class rewritten
/// to match it, and still have no way to be <em>told</em> about a class that did not — so CI could
/// not see it and neither could the Findings list (B181). Its siblings in the Ordering category all
/// report, and a settings row with no rule id behind it was also the one row in
/// <c>settings-reference.md</c> with no id to bind its label to (B103).</para>
///
/// <para><b>The section is the unit, not the class.</b> The renderer groups components before
/// classes within each <c>public</c> and <c>protected</c> section separately — it never moves an
/// element across the boundary — so a class definition in the public section followed by a
/// component in the protected one is correctly ordered and must not be reported. Comparing across
/// the whole class would report exactly the arrangement formatting produces.</para>
/// </summary>
public class ComponentsBeforeClasses : VisitorWithModelNameTracking
{
    /// <param name="basePackage">The package prefix to use when the code doesn't have a within clause.</param>
    public ComponentsBeforeClasses(string basePackage = "") : base(basePackage)
    {
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
    /// Reports every component declared after the first class definition in one section.
    ///
    /// <para>Every one of them, not just the first: the finding is about the component that is out
    /// of place, it carries that component's line, and reporting only the first would leave the rest
    /// invisible until it was fixed. An <c>import</c> or <c>extends</c> is neither a component nor a
    /// class and takes no part in this ordering — <c>MLQT.Style.ImportStatementsFirst</c> is the
    /// rule that has an opinion about those.</para>
    /// </summary>
    private void CheckSection(modelicaParser.Element_listContext elementList)
    {
        var classSeen = false;

        foreach (var element in elementList.element())
        {
            if (element.class_definition() != null)
            {
                classSeen = true;
            }
            else if (classSeen && element.component_clause() is { } component)
            {
                AddFinding(element.Start.Line,
                    $"Component '{NameOf(component)}' is declared after a nested class definition",
                    RuleIds.ComponentsBeforeClasses,
                    elementPath: NameOf(component));
            }
        }
    }

    /// <summary>
    /// The first name a component clause declares, for the message and the element path.
    /// A clause can declare several (<c>Real x, y;</c>); the first is enough to find it, and the
    /// whole clause moves as one, so it is one finding rather than one per name.
    /// </summary>
    private static string NameOf(modelicaParser.Component_clauseContext component) =>
        component.component_list()?.component_declaration()?.FirstOrDefault()
            ?.declaration()?.IDENT()?.GetText() ?? "?";
}
