namespace ModelicaParser.Helpers;

/// <summary>
/// <b>Which description and annotation belong to a declared component</b>, and where a missing one
/// goes. Ask here, never <c>declaration.comment()</c>.
/// </summary>
/// <remarks>
/// <para>For <c>replaceable Engine engine constrainedby Base "The engine" annotation(Placement(...));</c>
/// the description and annotation follow the constraining clause, and the grammar - as MLS 7.3.2
/// does - makes them the <i>element's</i> comment: the declaration's own is empty. That is how Dymola
/// and OMEdit write every replaceable component with a constraint. Readers that asked the declaration
/// drew such a component nowhere on a diagram and gave it no description, and writers that found no
/// annotation added a second one before <c>constrainedby</c>, leaving the real Placement as it was.</para>
///
/// <para>Each part is taken from after the constraining clause when it is there, and from the
/// declaration otherwise, so <c>replaceable A a "d" constrainedby B annotation(...)</c> - text written
/// by hand - is read whole as well.</para>
/// </remarks>
public static class ComponentComment
{
    /// <summary>The annotation that applies to the component, or null when it has none.</summary>
    public static modelicaParser.AnnotationContext? Annotation(modelicaParser.Component_declarationContext declaration) =>
        AfterConstraint(declaration)?.annotation() ?? declaration.comment()?.annotation();

    /// <summary>The description that applies to the component, or null when it has none.</summary>
    public static modelicaParser.String_commentContext? Description(modelicaParser.Component_declarationContext declaration)
    {
        if (AfterConstraint(declaration)?.string_comment() is { } after && after.STRING().Length > 0)
            return after;
        return declaration.comment()?.string_comment() is { } own && own.STRING().Length > 0 ? own : null;
    }

    /// <summary>
    /// Where an annotation the component does not have yet goes: after the constraining clause and its
    /// description when there is one, else after the declaration and its description.
    /// </summary>
    public static int AnnotationInsertOffset(modelicaParser.Component_declarationContext declaration) =>
        ConstrainedElement(declaration) is { } element
            ? element.Stop.StopIndex + 1
            : declaration.Stop.StopIndex + 1;

    /// <summary>
    /// Where a description the component does not have yet goes: after the constraining clause when
    /// there is one, else after the declaration and its condition - before any annotation either way.
    /// </summary>
    public static int DescriptionInsertOffset(modelicaParser.Component_declarationContext declaration)
    {
        if (ConstrainedElement(declaration) is { } element)
            return element.constraining_clause().Stop.StopIndex + 1;
        var last = (Antlr4.Runtime.ParserRuleContext?)declaration.condition_attribute() ?? declaration.declaration();
        return last.Stop.StopIndex + 1;
    }

    /// <summary>
    /// The other components whose description and annotation are this one's: those declared beside it
    /// before a constraining clause, as in <c>replaceable Engine e1, e2 constrainedby Base annotation(...)</c>,
    /// where the one comment after the clause belongs to the element and so to both. Empty otherwise,
    /// each component then having a comment of its own. A writer changing this component's
    /// annotation or description would change theirs too, and Modelica has no way to give them
    /// different ones short of splitting the declaration.
    /// </summary>
    public static IReadOnlyList<string> SharedWith(modelicaParser.Component_declarationContext declaration)
    {
        if (ConstrainedElement(declaration) is null || declaration.Parent is not modelicaParser.Component_listContext list)
            return [];
        return list.component_declaration()
            .Where(d => !ReferenceEquals(d, declaration))
            .Select(d => d.declaration()?.IDENT()?.GetText())
            .OfType<string>()
            .ToList();
    }

    /// <summary>
    /// The description a replaceable <i>class</i> carries after its constraining clause -
    /// <c>replaceable package Medium = Water constrainedby PartialMedium "The medium"</c> - or null when it
    /// has none there. The same rule as a component's, for a class written where its element is: the
    /// description rule read it, and the interface view, which did not, told an agent the class had none.
    /// </summary>
    public static modelicaParser.String_commentContext? ClassDescriptionAfterConstraint(modelicaParser.Class_specifierContext specifier) =>
        // class_specifier -> class_definition -> element
        specifier.Parent?.Parent is modelicaParser.ElementContext element
        && element.constraining_clause() is not null
        && element.comment()?.string_comment() is { } description
        && description.STRING().Length > 0
            ? description
            : null;

    // component_declaration -> component_list -> component_clause -> element: the only place a
    // component clause appears in the grammar.
    private static modelicaParser.ElementContext? ConstrainedElement(modelicaParser.Component_declarationContext declaration) =>
        declaration.Parent?.Parent?.Parent is modelicaParser.ElementContext element && element.constraining_clause() is not null
            ? element
            : null;

    private static modelicaParser.CommentContext? AfterConstraint(modelicaParser.Component_declarationContext declaration) =>
        ConstrainedElement(declaration)?.comment();
}
