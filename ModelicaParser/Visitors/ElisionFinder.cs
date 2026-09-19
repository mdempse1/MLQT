using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;

namespace ModelicaParser.Visitors;

/// <summary>
/// Finds the parts of a class that the viewer and the MCP tools hide, as
/// <see cref="SourceElision"/> line ranges.
///
/// <para>This is the tree-reading half of what <see cref="ModelicaRenderer"/> does when it is told
/// not to visit annotations or not to visit nested class definitions. The difference is that the
/// renderer rebuilds the whole text around what it skipped, while these ranges are dropped from the
/// source, leaving everything still shown exactly as it was written.</para>
///
/// <para><b>Whole lines only, and the construct is elided as a unit or not at all.</b> An annotation
/// that shares its first or last line with real code is left alone: removing part of a line would
/// leave the user looking at <c>Real x "d" annotation (Placement(</c>, which is worse than showing
/// the annotation. That is a deliberate limit rather than an approximation — in the Modelica
/// Standard Library it keeps the inline <c>Placement</c> annotations on declarations and hides the
/// class-level graphics annotations, which are the ones written on their own lines and the ones that
/// account for the volume (41–44% of lines sit wholly inside an annotation).</para>
/// </summary>
public static class ElisionFinder
{
    /// <summary>
    /// Every annotation in the class that occupies whole lines. Nested annotations are not returned
    /// separately — the outermost one covers them.
    /// </summary>
    /// <param name="marker">
    /// Given the name of what is being hidden (always <c>"annotation"</c> here), returns the single
    /// line to show in its place, or null to show nothing. The line is spliced in beside the lines
    /// that were kept, so it has to be in the same language as them — markup if the caller is
    /// eliding highlighted lines, plain text if it is eliding source.
    /// </param>
    public static SourceElision Annotations(
        modelicaParser.Stored_definitionContext? tree, string source, Func<string, string?>? marker = null)
    {
        if (tree is null)
            return SourceElision.None;

        var lines = ModelicaParserHelper.NormalizeLineEndings(source).Split('\n');
        var ranges = new List<ElidedRange>();
        Collect(tree, lines, ranges, marker, "annotation",
            isTarget: static (node, _) => node is modelicaParser.AnnotationContext);
        return SourceElision.Of(ranges);
    }

    /// <summary>
    /// Every class definition nested directly inside a top-level class — the ones the viewer hides
    /// for a package. Classes nested deeper are inside one of these and are covered by it.
    /// </summary>
    /// <param name="marker">
    /// Given the nested class's name, returns the line to show in its place, or null to show
    /// nothing. See <see cref="Annotations"/> for what language that line has to be in.
    /// </param>
    public static SourceElision NestedClasses(
        modelicaParser.Stored_definitionContext? tree, string source, Func<string, string?>? marker = null)
    {
        if (tree is null)
            return SourceElision.None;

        var lines = ModelicaParserHelper.NormalizeLineEndings(source).Split('\n');
        var ranges = new List<ElidedRange>();
        Collect(tree, lines, ranges, marker, name: null,
            isTarget: static (node, depth) =>
                node is modelicaParser.Class_definitionContext && depth == 1);
        return SourceElision.Of(ranges);
    }

    /// <summary>
    /// Walks to the outermost matching contexts, recording a range for each one that occupies whole
    /// lines, and never descending into one it has matched.
    /// </summary>
    /// <param name="isTarget">
    /// Whether this node is one to elide, given how many <c>class_definition</c> contexts enclose it.
    /// </param>
    private static void Collect(
        IParseTree node,
        string[] lines,
        List<ElidedRange> ranges,
        Func<string, string?>? marker,
        string? name,
        Func<IParseTree, int, bool> isTarget,
        int classDepth = 0)
    {
        if (node is ParserRuleContext context && isTarget(node, classDepth))
        {
            if (TryWholeLines(context, lines, out var first, out var last))
                ranges.Add(new ElidedRange(first, last, marker?.Invoke(name ?? NameOf(context))));
            return;
        }

        if (node is modelicaParser.Class_definitionContext)
            classDepth++;

        for (var i = 0; i < node.ChildCount; i++)
            Collect(node.GetChild(i), lines, ranges, marker, name, isTarget, classDepth);
    }

    /// <summary>
    /// Whether <paramref name="context"/> has its first and last lines to itself — nothing but
    /// whitespace before it on the first, and nothing but whitespace and an optional statement
    /// semicolon after it on the last.
    /// </summary>
    private static bool TryWholeLines(
        ParserRuleContext context, string[] lines, out int firstLine, out int lastLine)
    {
        firstLine = context.Start?.Line ?? 0;
        lastLine = context.Stop?.Line ?? 0;

        if (firstLine < 1 || lastLine < firstLine || lastLine > lines.Length)
            return false;

        var before = lines[firstLine - 1].AsSpan(0, Math.Min(context.Start!.Column, lines[firstLine - 1].Length));
        if (!before.IsWhiteSpace())
            return false;

        var lastText = lines[lastLine - 1];
        var endColumn = context.Stop!.Column + (context.Stop.StopIndex - context.Stop.StartIndex) + 1;
        if (endColumn > lastText.Length)
            return false;

        var after = lastText.AsSpan(endColumn).TrimStart();
        if (after.StartsWith(";"))
            after = after[1..];

        return after.IsWhiteSpace();
    }

    /// <summary>The declared name of a class definition, for a marker to mention.</summary>
    private static string NameOf(ParserRuleContext context)
    {
        // Reached only from NestedClasses, whose predicate has already matched a class definition.
        var specifier = ((modelicaParser.Class_definitionContext)context).class_specifier();
        return specifier?.long_class_specifier()?.IDENT(0)?.GetText()
               ?? specifier?.short_class_specifier()?.IDENT()?.GetText()
               ?? specifier?.der_class_specifier()?.IDENT(0)?.GetText()
               ?? "";
    }
}
