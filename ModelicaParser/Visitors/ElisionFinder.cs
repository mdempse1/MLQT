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
    /// The source with every annotation taken out, and the lines that vanished with them.
    ///
    /// <para><see cref="Annotations"/> hides a construct as a unit or not at all, which is right
    /// when the alternative is leaving the user looking at <c>Real x "d" annotation (Placement(</c>
    /// — but it means an annotation sharing a line with code stays on screen. Measured over 8,367
    /// files of real Modelica, <b>31.6% of non-blank equation-section lines carry an annotation and
    /// 62% of those were left visible</b>, because a <c>connect(...)</c> almost always writes its
    /// <c>annotation(Line(...))</c> on the same line. Turning annotations off changed nothing in
    /// the section holding most of the noise (B233).</para>
    ///
    /// <para><b>It cuts text, not markup.</b> The alternative was splicing highlighted lines at a
    /// character offset, which can cut inside a tag and needs the classifier to say where each
    /// token's markup begins. Removing an annotation from Modelica leaves Modelica, so the caller
    /// can simply highlight what comes back — one more parse, and no second representation of where
    /// a token starts.</para>
    ///
    /// <para><b>Line numbers are preserved</b>, which is what lets a finding still point at the
    /// right line. An annotation running across lines is spliced onto its first line and the rest
    /// of its lines are returned as elided, so the caller's existing line map does that half.</para>
    /// </summary>
    /// <param name="elidedTextMustParse">
    /// Whether the caller will drop the elided lines and then parse, or hand on, what is left.
    ///
    /// <para>It changes one thing: who owns the semicolon. An annotation written on its own lines
    /// is usually followed by one, and in <c>annotation (Placement(...));</c> under a declaration
    /// that semicolon terminates the <em>declaration</em> — taking it out with the annotation runs
    /// the declaration into the next one. The same is true of an extends clause, an equation and an
    /// external clause; only the class-level <c>annotation ';'</c> owns its own. With this set, a
    /// semicolon that is not the annotation's is kept, so what remains is still Modelica.</para>
    ///
    /// <para><b>The viewer leaves it false on purpose.</b> Keeping it puts a line holding nothing
    /// but <c>;</c> on screen for every hidden declaration annotation, which in a library of any
    /// size is precisely the per-annotation noise B233 was asked to remove — and the viewer never
    /// parses the elided text, only the spliced source, which still has those annotations in it.
    /// <c>get_class_source</c> sets it because an agent reads the elided text and edits it (B218).</para>
    /// </param>
    /// <returns>
    /// The spliced source, which has exactly as many lines as it was given, and the lines to drop
    /// from it — whole-line annotations, plus the continuation lines of the ones spliced.
    /// </returns>
    public static (string Source, SourceElision Elision) WithoutAnnotations(
        modelicaParser.Stored_definitionContext? tree, string source, bool elidedTextMustParse = false)
    {
        if (tree is null)
            return (source, SourceElision.None);

        var lines = ModelicaParserHelper.NormalizeLineEndings(source).Split('\n');

        var annotations = new List<modelicaParser.AnnotationContext>();
        CollectOutermost(tree, annotations);

        var ranges = new List<ElidedRange>();
        var spliced = (string[])lines.Clone();

        // Last first, so a cut never moves a column an earlier cut was measured against. Two
        // annotations on one line is unusual but legal — two declarations, each with its own.
        foreach (var annotation in annotations
                     .OrderByDescending(a => a.Start?.Line ?? 0)
                     .ThenByDescending(a => a.Start?.Column ?? 0))
        {
            var ownsSemicolon = !elidedTextMustParse || OwnsItsSemicolon(annotation);

            if (TryWholeLines(annotation, lines, out var first, out var last, ownsSemicolon))
            {
                ranges.Add(new ElidedRange(first, last, null));
                continue;
            }

            if (!TrySpan(annotation, spliced, out first, out last, out var startColumn, out var endColumn))
                continue;

            // What was before it on its first line, joined to what was after it on its last. When
            // those are different lines the ones between go, and so does the tail of the last.
            //
            // The head keeps its indentation when there is nothing else on the line, so an
            // annotation spliced only because it does not own the semicolon after it leaves that
            // semicolon where the reader expects it rather than in column 1.
            var before = spliced[first - 1][..startColumn];
            var head = before.Trim().Length == 0 ? before : before.TrimEnd();
            var tail = spliced[last - 1][endColumn..];

            // A class-level annotation sharing its line with code is spliced like any other, and
            // its semicolon has to go with it or the composition is left holding a bare `;`.
            if (elidedTextMustParse && ownsSemicolon)
            {
                var afterAnnotation = tail.AsSpan().TrimStart();
                if (afterAnnotation.StartsWith(";"))
                    tail = afterAnnotation[1..].ToString();
            }

            spliced[first - 1] = head + tail;

            if (last > first)
            {
                // **Emptied, not left to the elision.** The lines are reported as elided so the
                // caller drops them, but the text returned here has to stand on its own: a caller
                // that parses it — which is the point, since the colouring comes from the tree —
                // would otherwise be handed the orphaned tail of an annotation whose head has gone.
                // Measured over 8,367 files, leaving them in place broke the parse of 3,199 of them
                // and would have quietly dropped the viewer to lexer-only colouring for each.
                for (var line = first; line < last; line++)
                    spliced[line] = "";

                ranges.Add(new ElidedRange(first + 1, last, null));
            }
        }

        return (string.Join('\n', spliced), SourceElision.Of(ranges));
    }

    /// <summary>Where a context starts and stops, as line/column into <paramref name="lines"/>.</summary>
    private static bool TrySpan(
        ParserRuleContext context, string[] lines,
        out int firstLine, out int lastLine, out int startColumn, out int endColumn)
    {
        firstLine = context.Start?.Line ?? 0;
        lastLine = context.Stop?.Line ?? 0;
        startColumn = context.Start?.Column ?? 0;
        endColumn = 0;

        if (firstLine < 1 || lastLine < firstLine || lastLine > lines.Length)
            return false;

        if (startColumn > lines[firstLine - 1].Length)
            return false;

        endColumn = context.Stop!.Column + (context.Stop.StopIndex - context.Stop.StartIndex) + 1;
        return endColumn <= lines[lastLine - 1].Length;
    }

    /// <summary>
    /// Every annotation not inside another one. The outer covers the inner, and a nested annotation
    /// returned separately would be spliced twice.
    /// </summary>
    private static void CollectOutermost(IParseTree node, List<modelicaParser.AnnotationContext> found)
    {
        if (node is modelicaParser.AnnotationContext annotation)
        {
            found.Add(annotation);
            return;
        }

        for (var i = 0; i < node.ChildCount; i++)
            CollectOutermost(node.GetChild(i), found);
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
    /// <param name="consumeTrailingSemicolon">
    /// Whether a semicolon after the context counts as part of it. It does for anything removed as
    /// a whole element — a nested class, a class-level annotation — and does not for an annotation
    /// attached to something that the semicolon terminates.
    /// </param>
    private static bool TryWholeLines(
        ParserRuleContext context, string[] lines, out int firstLine, out int lastLine,
        bool consumeTrailingSemicolon = true)
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
        if (consumeTrailingSemicolon && after.StartsWith(";"))
            after = after[1..];

        return after.IsWhiteSpace();
    }

    /// <summary>
    /// Whether the semicolon after <paramref name="annotation"/> belongs to the annotation rather
    /// than to whatever it is attached to.
    ///
    /// <para>The grammar writes <c>annotation ';'</c> only in <c>composition</c>, at either end of
    /// a class body. Everywhere else — a declaration's or an equation's <c>comment</c>, an
    /// <c>extends_clause</c>, and the <c>'external' ... (annotation)? ';'</c> form, where the
    /// semicolon closes the external clause — the annotation is part of a larger element and the
    /// semicolon closes that element.</para>
    /// </summary>
    private static bool OwnsItsSemicolon(modelicaParser.AnnotationContext annotation)
    {
        if (annotation.Parent is not modelicaParser.CompositionContext composition)
            return false;

        // Both the external clause's annotation and the class's are direct children of the
        // composition, so the parent alone does not separate them. Walking back to the nearest
        // terminal does: the external one has `external` behind it and no semicolon since.
        var index = -1;
        for (var i = 0; i < composition.ChildCount; i++)
            if (ReferenceEquals(composition.GetChild(i), annotation))
            {
                index = i;
                break;
            }

        for (var i = index - 1; i >= 0; i--)
        {
            if (composition.GetChild(i) is not ITerminalNode terminal)
                continue;
            if (terminal.GetText() == ";")
                return true;
            if (terminal.GetText() == "external")
                return false;
        }

        return true;
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
