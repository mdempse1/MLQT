using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaGraph;

/// <summary>
/// Trims each package's stored <c>ModelicaCode</c> to exclude its standalone child classes (which have
/// their own graph nodes), freeing the duplicated inline source for large libraries. Shared by the GUI
/// startup and the headless CLI/MCP so all paths style-check the same representation.
///
/// The trimmed source is stored WITHOUT a leading <c>within</c> clause: the <c>within</c> is only needed
/// transiently to parse the class in isolation, and keeping it would shift every finding's line number
/// by one relative to the original file (the number a user sees in their Modelica tool).
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
            children.Any(c => c.CanBeStoredStandalone)).ToList();
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

                // Prepend a within clause only so the class parses in isolation (required by the grammar
                // for name resolution during rendering).
                var codeToParse = WithinClause.Ensure(model.Definition.ModelicaCode, model.ParentModelName);

                var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(codeToParse);
                if (parseTree == null || errors.Count > 0)
                    return;

                // Short class definitions (X = Y) have no body to trim.
                foreach (var classDef in parseTree.class_definition())
                    if (classDef.class_specifier()?.short_class_specifier() != null)
                        return;

                var visitor = new ModelicaRenderer(
                    renderForCodeEditor: false,
                    showAnnotations: true,
                    excludeClassDefinitions: false,
                    tokenStream: null,
                    classNamesToExclude: standaloneNames,
                    oneOfEachSection: false,
                    importsFirst: false,
                    componentsBeforeClasses: false);
                visitor.VisitStored_definition(parseTree);
                var trimmedCode = string.Join("\n", visitor.Code);

                // Drop the leading within line so stored line numbers align with the source file.
                model.Definition.ModelicaCode = WithinClause.Strip(trimmedCode);
                model.Definition.ParsedCode = null; // release the parse tree

                // The children are gone and the rest has been through the renderer, so a line in this
                // text is no longer the file's line. Reports fall back to the class declaration
                // rather than pointing confidently at the wrong line — see ModelNode.SourceMatchesFile.
                model.SourceMatchesFile = false;
            }
            catch
            {
                // If trimming a model fails, keep the original — it's still valid.
            }
        });
    }
}
