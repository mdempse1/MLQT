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

                // Build the set of standalone child names to exclude (same rule as ModelicaPackageSaver:
                // a standalone class whose (case-insensitive) name is unique among the siblings).
                var nameCounts = children
                    .GroupBy(c => c.Definition.Name.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.Count());

                var standaloneNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var child in children)
                {
                    var lowerName = child.Definition.Name.ToLowerInvariant();
                    if (child.CanBeStoredStandalone && nameCounts[lowerName] == 1 && lowerName != "package")
                        standaloneNames.Add(child.Definition.Name);
                }
                if (standaloneNames.Count == 0)
                    return;

                // Excised rather than rendered away (B216). Cutting the children's lines out leaves
                // every line that is still there exactly as the user wrote it, so the text is a
                // subsequence of the file and the relation between the two is a SourceElision —
                // monotone, so mapping a finding's line back is arithmetic. Re-rendering instead
                // rebuilt the whole package as a side effect of removing part of it, and the line
                // mapping went with it: every finding in a trimmed package pointed at the class
                // declaration because nothing could say where it really was.
                var lines = model.Definition.ModelicaCode.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

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
                    if (!OwnsItsLines(lines, first, last))
                        continue;

                    ranges.Add(new ElidedRange(first, last, Replacement: null));
                }

                if (ranges.Count == 0)
                    return;

                var elision = SourceElision.Of(ranges);

                // Setting ModelicaCode releases the parse tree with it — a tree read from the old
                // source describes code that is no longer here.
                model.Definition.ModelicaCode = string.Join("\n", elision.Apply(lines));
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
    /// Whether the class occupying lines <paramref name="first"/>..<paramref name="last"/> has those
    /// lines to itself, so dropping them takes nothing else with it.
    ///
    /// <para>A class written as <c>model A end A; model B end B;</c> on one line shares it with its
    /// neighbour; so does one whose <c>end</c> is followed by a comment about the next declaration.
    /// Neither can be excised, and the whole point of excising is that what is left is untouched, so
    /// such a child is simply kept. It is then still nested inside the package's source, which is
    /// harmless: a rule visitor skips a nested standalone class definition because it has its own
    /// node and is checked there.</para>
    /// </summary>
    private static bool OwnsItsLines(string[] lines, int first, int last)
    {
        var opening = lines[first - 1].TrimStart();
        var closing = lines[last - 1].TrimEnd();

        // The parser's start line is the class_definition, which begins at its first keyword — so
        // anything before it on that line belongs to something else. The stop line ends at `end X`,
        // and the `;` after it is the only thing allowed to follow.
        return opening.Length > 0
            && lines[first - 1].AsSpan(0, lines[first - 1].Length - opening.Length).IsWhiteSpace()
            && closing.EndsWith(';');
    }
}
