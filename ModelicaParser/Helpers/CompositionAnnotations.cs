using Antlr4.Runtime.Tree;

namespace ModelicaParser.Helpers;

/// <summary>
/// Which of a class body's annotations is which: the leading class annotation, the external
/// clause's, and the trailing class annotation.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (B446).</b> The grammar gives a <c>composition</c> up to three
/// annotations, all direct children of it:</para>
/// <code>
/// composition
///     : (c_comment* annotation ';')?                        // leading, a Dymola extension
///       element_list ( ... )*
///       ('external' ... (annotation)? ';')?                 // the external clause's
///       c_comment* ( annotation ';' )?                      // trailing, the class's own
///       final_comment ;
/// </code>
/// <para>so <c>composition.annotation()</c> lists whichever are present, in source order, and its
/// index says nothing about which one an entry is. Readers that asked for <c>[0]</c> meaning "the
/// external clause's" or <c>[^1]</c> meaning "the class's" were right only when the others were
/// absent: the formatter gave a function's leading <c>annotation(Inline=true)</c> to its external
/// clause and moved <c>Library="lib"</c> to the class, changing what the function links against.
/// Ask here by position in the tree instead.</para>
/// <para>An annotation alone in its body (<c>model M annotation(Icon()); end M;</c>) is parsed as the
/// leading one, because the optional leading <c>annotation ';'</c> matches before the empty element
/// list. With nothing after it, it stands where the trailing one does, and it is reported as that
/// (B457): taken for a leading one, an edit adding the class's first element wrote it after the
/// annotation. Its comments are still before the element list in the tree, which is why the renderer
/// asks for them by position.</para>
/// </remarks>
public static class CompositionAnnotations
{
    /// <summary>The three annotations of one class body, each null when absent.</summary>
    public readonly record struct Parts(
        modelicaParser.AnnotationContext? Leading,
        modelicaParser.AnnotationContext? External,
        modelicaParser.AnnotationContext? Trailing)
    {
        /// <summary>
        /// The class's own annotations - leading then trailing - without the external clause's.
        /// </summary>
        public IReadOnlyList<modelicaParser.AnnotationContext> ClassLevel =>
            (Leading, Trailing) switch
            {
                (null, null) => [],
                ({ } leading, null) => [leading],
                (null, { } trailing) => [trailing],
                ({ } leading, { } trailing) => [leading, trailing],
            };

        /// <summary>
        /// The class annotation a writer should add to: the trailing one, which is where Modelica
        /// puts it, else the leading one.
        /// </summary>
        public modelicaParser.AnnotationContext? Class => Trailing ?? Leading;
    }

    /// <summary>Sorts <paramref name="composition"/>'s annotations by where they stand in it.</summary>
    public static Parts Of(modelicaParser.CompositionContext? composition)
    {
        modelicaParser.AnnotationContext? leading = null, external = null, trailing = null;
        if (composition?.children is not { } children)
            return default;

        var sawElementList = false;
        var inExternalClause = false;
        var anythingAfterTheLists = false;
        foreach (var child in children)
        {
            switch (child)
            {
                case modelicaParser.Element_listContext list:
                    sawElementList = true;
                    if (list.element().Length > 0)
                        anythingAfterTheLists = true;
                    break;
                case modelicaParser.Equation_sectionContext or modelicaParser.Algorithm_sectionContext:
                    anythingAfterTheLists = true;
                    break;
                case ITerminalNode:
                    var keyword = SectionKeyword.Of(child);
                    if (keyword == "external")
                        inExternalClause = true;
                    else if (keyword == ";")
                        inExternalClause = false;
                    if (keyword is "external" or "public" or "protected")
                        anythingAfterTheLists = true;
                    break;
                case modelicaParser.AnnotationContext annotation:
                    if (inExternalClause)
                        external = annotation;
                    else if (!sawElementList)
                        leading = annotation;
                    else
                        trailing = annotation;
                    break;
            }
        }

        // A body holding nothing but its annotation (B457): the grammar's optional leading
        // `annotation ';'` matches before the empty element list, but with nothing after it the
        // annotation stands where a trailing one does, and it is one - a new first element goes
        // before it, not after.
        if (leading is not null && trailing is null && !anythingAfterTheLists)
            (leading, trailing) = (null, leading);

        return new Parts(leading, external, trailing);
    }

    /// <summary>The external clause's annotation, or null when there is no clause or it has none.</summary>
    public static modelicaParser.AnnotationContext? External(modelicaParser.CompositionContext? composition)
        => Of(composition).External;

    /// <summary>The class's own annotations, leading then trailing, never the external clause's.</summary>
    public static IReadOnlyList<modelicaParser.AnnotationContext> ClassLevel(modelicaParser.CompositionContext? composition)
        => Of(composition).ClassLevel;

    /// <summary>True when <paramref name="annotation"/> is the external clause's annotation.</summary>
    public static bool IsExternal(modelicaParser.AnnotationContext annotation)
        => annotation.Parent is modelicaParser.CompositionContext composition
           && ReferenceEquals(External(composition), annotation);
}
