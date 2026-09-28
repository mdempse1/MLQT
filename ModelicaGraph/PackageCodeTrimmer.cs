using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph;

/// <summary>
/// Trims each package's stored <c>ModelicaCode</c> to exclude its standalone child classes (which have
/// their own graph nodes), freeing the duplicated inline source for large libraries. Shared by the GUI
/// startup and the headless CLI/MCP so all paths style-check the same representation.
///
/// <para><b>It cuts the children's lines out; it does not rebuild the package.</b> Everything left is
/// the file's own text, character for character, and <see cref="ModelNode.TrimElision"/> records
/// which lines went — so a finding's line still maps to a line in the file (B216). Re-rendering the
/// package instead, which is what this did, rewrote the whole of it as a side effect of removing
/// part of it: the mapping was gone, and every finding in a trimmed package was reported at the
/// class declaration because nothing could say where it really was. Measured over the Modelica
/// Standard Library, that was 432 findings; over Buildings, 34.</para>
///
/// <para>The excision is also why nothing here has to think about the <c>within</c> clause any more.
/// A package's stored code never carries one, and since the text is no longer parsed and re-emitted
/// there is no round trip to add one and strip it off again.</para>
/// </summary>
public static class PackageCodeTrimmer
{
    /// <summary>Trim the packages in <paramref name="graph"/>. When <paramref name="onlyModelIds"/> is
    /// given, only packages in that set are trimmed (e.g. the models of a just-loaded library), so
    /// repeated loads don't re-trim everything.</summary>
    public static void TrimStandaloneChildren(DirectedGraph graph, IReadOnlySet<string>? onlyModelIds = null)
    {
        var allModels = graph.ModelNodes.ToList();

        var childrenByParent = new Dictionary<string, List<ModelNode>>(StringComparer.Ordinal);
        foreach (var model in allModels)
        {
            var parentName = model.ParentModelName;
            if (!string.IsNullOrEmpty(parentName))
            {
                if (!childrenByParent.TryGetValue(parentName, out var list))
                    childrenByParent[parentName] = list = new List<ModelNode>();
                list.Add(model);
            }
        }

        var packagesToTrim = allModels.Where(m =>
            m.ClassType == "package" &&
            // A stub's source is a synthesized declaration, not the vendor's text: it has no inline
            // children to trim out, and re-rendering it would be rewriting our own reconstruction.
            !m.IsExternalStub &&
            !m.ChildrenTrimmed &&
            (onlyModelIds is null || onlyModelIds.Contains(m.Id)) &&
            childrenByParent.TryGetValue(m.Id, out var children) &&
            // The child has to be inline in THIS package's own file. A standalone child already
            // stored in its own file is not in the package's source to begin with, so the render
            // below would exclude it from a tree it was never in and write the package back
            // unchanged in meaning but rebuilt in text — losing the line mapping
            // (ModelNode.SourceMatchesFile) for nothing. That was the majority of the work and all
            // of the damage: 377 of 688 trimmed packages in MSL and 1,172 of 1,235 in Buildings,
            // where 1,073 came out longer than the file they came from (backlog B230).
            //
            // Sharing the file is the cheap half of the question, and it stays the one asked here.
            // The exact one — whether the child's lines fall inside the package's — is asked per
            // child below, where it has to be anyway before a range can be cut out. Asking it twice
            // would mean walking every child of every package to decide whether to walk them again.
            children.Any(c => c.CanBeStoredStandalone &&
                              string.Equals(c.ContainingFileId, m.ContainingFileId, StringComparison.Ordinal))).ToList();
        if (packagesToTrim.Count == 0)
            return;

        Parallel.ForEach(packagesToTrim, model =>
        {
            // Marked whatever the outcome (trimmed, nothing to trim, unparseable): the work has been
            // attempted for this source, and repeating it would produce the same answer. A reload
            // replaces the node, so reloaded source is considered afresh.
            model.ChildrenTrimmed = true;

            try
            {
                var children = childrenByParent[model.Id];

                // The children the saver writes as their own entries, asked of the one rule that
                // decides it. This carried a copy of the rule B245 replaced — class names compared
                // case-insensitively — so a package and a model differing only in case were written
                // out by the saver and kept inline here (B309).
                var standaloneNames = PackageFileLayout.StandaloneChildNames(children);
                if (standaloneNames.Count == 0)
                    return;

                // Excised rather than rendered away (B216). Cutting the children's lines out leaves
                // every line that is still there exactly as the user wrote it, so the text is a
                // subsequence of the file and the relation between the two is a SourceElision —
                // monotone, so mapping a finding's line back is arithmetic. Re-rendering instead
                // rebuilt the whole package as a side effect of removing part of it, and the line
                // mapping went with it: every finding in a trimmed package pointed at the class
                // declaration because nothing could say where it really was.
                var code = model.Definition.ModelicaCode.Replace("\r\n", "\n").Replace('\r', '\n');
                var lines = code.Split('\n');
                var lineStarts = LineStarts(code);

                var ranges = new List<ElidedRange>();
                foreach (var child in children)
                {
                    if (!standaloneNames.Contains(child.Definition.Name))
                        continue;

                    // Only a child that is actually inline in this package's file. A child already
                    // in its own file is not in this text at all, and its line numbers are measured
                    // against that file — subtracting this package's start line from them produces a
                    // range that means nothing here, and on a package with one child of each kind
                    // the two ranges collided and the whole trim was abandoned. This is B230's
                    // question asked per child rather than per package.
                    if (!string.Equals(child.ContainingFileId, model.ContainingFileId, StringComparison.Ordinal))
                        continue;

                    // The child's lines relative to this package's own source. Line numbers, not the
                    // character offsets the item named: both are on the node, and lines are what an
                    // elision is made of — going through offsets would mean converting back.
                    var first = child.StartLine - model.StartLine + 1;
                    var last = child.StopLine - model.StartLine + 1;

                    // Only a child that owns its lines. One sharing a line with something else
                    // cannot be dropped without taking that with it, and ElisionFinder makes the
                    // same call for the same reason: a construct goes as a unit or not at all.
                    if (first < 2 || last < first || last >= lines.Length)
                        continue;
                    if (!OwnsItsLines(code, lineStarts, model, child, first, last, out var through))
                        continue;

                    ranges.Add(new ElidedRange(first, through, Replacement: null));
                }

                if (ranges.Count == 0)
                    return;

                var elision = SourceElision.Of(ranges);

                // Setting ModelicaCode releases the parse tree with it — a tree read from the old
                // source describes code that is no longer here.
                //
                // It also drops the class's parse errors and lifts the bar on recording its own
                // (B389), which is right for new code and wrong for this: cutting lines out changes
                // nothing about what the load found in the lines that are left, and lifting the bar
                // would let this package record a second copy of an error the load gave to a class
                // inside it. So the load's diagnosis is put back. Anything the class recorded by
                // parsing its own source is not - its lines count from the text that was just cut,
                // and parsing what is left records them again.
                var loadErrors = model.Definition.ParserErrors.Where(e => !e.LineIsClassRelative).ToList();
                var mayRecord = model.Definition.MayRecordParserErrors;
                model.Definition.ModelicaCode = string.Join("\n", elision.Apply(lines));
                model.Definition.ParserErrors = loadErrors;
                model.Definition.MayRecordParserErrors = mayRecord;
                model.TrimElision = elision;

                // Still the file's own text, so a line in it still maps to a line in the file —
                // through the elision rather than by adding the offset. See ClassLocation.FileLine.
                model.SourceMatchesFile = true;
            }
            catch
            {
                // If trimming a model fails, keep the original — it's still valid.
            }
        });
    }

    /// <summary>
    /// Cuts the nested classes named in <paramref name="names"/> out of a package's text, leaving every
    /// other line exactly as written — for the saver's verbatim path, which writes a package that
    /// opted out of formatting as it stands and must not also carry a child it writes as its own file
    /// (B309). The trim above is the same operation on the stored source; this one works from a tree
    /// of <paramref name="code"/>, because a verbatim package may not have been trimmed at all.
    /// </summary>
    /// <param name="code">The package's text, as <paramref name="tree"/> was parsed from.</param>
    /// <param name="keptInline">The named classes found in the text that could not be cut out,
    /// because they share a line with something else. The caller must not write these separately:
    /// they are still here.</param>
    public static string ExciseInlineClasses(
        string code,
        modelicaParser.Stored_definitionContext tree,
        IReadOnlySet<string> names,
        out HashSet<string> keptInline)
    {
        keptInline = new HashSet<string>(StringComparer.Ordinal);

        var composition = tree.class_definition().FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition is null || names.Count == 0)
            return code;

        var lines = code.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var ranges = new List<ElidedRange>();

        foreach (var list in composition.element_list())
        {
            for (var i = 0; i < list.ChildCount; i++)
            {
                if (list.GetChild(i) is not modelicaParser.ElementContext element)
                    continue;

                var name = NameOf(element.class_definition());
                if (name is null || !names.Contains(name))
                    continue;

                // The element (so a prefix such as `final` goes with it) and the `;` that ends it,
                // which the element list holds rather than the element.
                var terminator = (list.GetChild(i + 1) as Antlr4.Runtime.Tree.ITerminalNode)?.Symbol;
                if (terminator is not null && OwnsItsLines(lines, element.Start, terminator))
                    ranges.Add(new ElidedRange(element.Start.Line, terminator.Line, Replacement: null));
                else
                    keptInline.Add(name);
            }
        }

        if (ranges.Count == 0)
            return code;

        // Joined with the endings the text came with: this is written verbatim, and a file whose
        // endings changed is a file every commit dialog reports as modified (B251).
        var newline = code.Contains("\r\n") ? "\r\n" : "\n";
        return string.Join(newline, SourceElision.Of(ranges).Apply(lines));
    }

    /// <summary>
    /// Whether the text from <paramref name="start"/> to <paramref name="end"/> (inclusive) has its
    /// lines to itself: nothing but whitespace before it on its first line or after it on its last.
    /// Asked by token position rather than by the line-based "the last line ends with a semicolon"
    /// the trim above once asked (B379), which <c>model A end A; model B end B;</c> satisfies for
    /// both classes — and cutting either line out would take the other with it.
    /// </summary>
    private static bool OwnsItsLines(string[] lines, Antlr4.Runtime.IToken start, Antlr4.Runtime.IToken end)
    {
        if (start.Line < 2 || end.Line < start.Line || end.Line > lines.Length)
            return false;

        var firstLine = lines[start.Line - 1];
        var lastLine = lines[end.Line - 1];
        var afterEnd = end.Column + 1;
        return start.Column <= firstLine.Length
            && firstLine.AsSpan(0, start.Column).IsWhiteSpace()
            && afterEnd <= lastLine.Length
            && lastLine.AsSpan(afterEnd).IsWhiteSpace();
    }

    /// <summary>The name a nested class definition declares, or null for anything else.</summary>
    private static string? NameOf(modelicaParser.Class_definitionContext? definition)
    {
        var specifier = definition?.class_specifier();
        return specifier?.long_class_specifier()?.IDENT(0)?.GetText()
            ?? specifier?.short_class_specifier()?.IDENT()?.GetText()
            ?? specifier?.der_class_specifier()?.IDENT(0)?.GetText();
    }

    /// <summary>
    /// Whether <paramref name="child"/>, inline in <paramref name="package"/>'s text
    /// <paramref name="code"/>, has its lines to itself, so dropping them takes nothing else with it —
    /// and if so, <paramref name="through"/> is the line holding the <c>;</c> that ends it.
    ///
    /// <para>A class written as <c>model A end A; model B end B;</c> on one line shares it with its
    /// neighbour; so does one whose <c>end</c> is followed by a comment about the next declaration.
    /// Neither can be excised, and the whole point of excising is that what is left is untouched, so
    /// such a child is simply kept. It is then still nested inside the package's source, which is
    /// harmless: a rule visitor skips a nested standalone class definition because it has its own
    /// node and is checked there.</para>
    ///
    /// <para><b>Asked by character position, as <see cref="ExciseInlineClasses"/> asks it by token
    /// (B379).</b> This used to look at whole lines and ask only that the last one end with a
    /// <c>;</c> — which <c>model A end A; model B end B;</c> satisfies for both classes. Both were
    /// offered for the same line, the ranges overlapped, <see cref="SourceElision.Of"/> threw, and the
    /// whole package went untrimmed; and <c>model A end A; constant Real k = 1;</c> satisfied it for
    /// the one class, so the constant was cut out of the checked text with it. The nodes' offsets are
    /// into the text the package's source was sliced from, so the child's own characters say where it
    /// starts and stops on its lines. Where they disagree with its line numbers the stored text is no
    /// longer that slice, and the child is kept rather than cut by guesswork.</para>
    /// </summary>
    private static bool OwnsItsLines(
        string code, int[] lineStarts, ModelNode package, ModelNode child, int first, int last, out int through)
    {
        through = 0;
        if (package.StartIndex < 0 || child.StartIndex < 0 || child.StopIndex < child.StartIndex)
            return false;

        var start = child.StartIndex - package.StartIndex;
        var stop = child.StopIndex - package.StartIndex;
        if (start < 0 || stop >= code.Length)
            return false;
        if (LineOf(lineStarts, start) != first || LineOf(lineStarts, stop) != last)
            return false;

        // Nothing but indentation before the class on its first line. The class_definition starts
        // at its first keyword, so anything else there belongs to something else.
        if (!code.AsSpan(lineStarts[first - 1], start - lineStarts[first - 1]).IsWhiteSpace())
            return false;

        // The rule stops at `end X` (or a short class's last token); the `;` that ends the element
        // follows it, possibly after whitespace, and nothing but whitespace may follow that on its
        // own line.
        var semicolon = stop + 1;
        while (semicolon < code.Length && char.IsWhiteSpace(code[semicolon]))
            semicolon++;
        if (semicolon >= code.Length || code[semicolon] != ';')
            return false;

        var lineEnd = code.IndexOf('\n', semicolon);
        if (lineEnd < 0)
            lineEnd = code.Length;
        if (!code.AsSpan(semicolon + 1, lineEnd - semicolon - 1).IsWhiteSpace())
            return false;

        // The package's own `end P;` is on its last line, so a child's `;` never is.
        through = LineOf(lineStarts, semicolon);
        return through < lineStarts.Length;
    }

    /// <summary>The offset at which each line of <paramref name="code"/> starts, first line first.</summary>
    private static int[] LineStarts(string code)
    {
        var starts = new List<int> { 0 };
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n')
                starts.Add(i + 1);
        }
        return starts.ToArray();
    }

    /// <summary>The one-based line holding <paramref name="offset"/>.</summary>
    private static int LineOf(int[] lineStarts, int offset)
    {
        var index = Array.BinarySearch(lineStarts, offset);
        return index >= 0 ? index + 1 : ~index;
    }
}
