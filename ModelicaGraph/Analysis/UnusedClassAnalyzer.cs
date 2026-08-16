using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags a <b>protected</b> nested class that nothing references (empty <c>UsedByModelIds</c>) — dead
/// code that, being protected, cannot be used from outside its enclosing class, so this is
/// high-confidence with no external-visibility false positives. Needs dependency analysis (the
/// reverse edges are analyzer-only). Public and top-level classes are deliberately not flagged: a
/// downstream library we cannot see may use them ("possibly unused API" is a separate, lower-confidence
/// concern). Partial classes (extended, not instantiated) and packages are skipped.
/// </summary>
public sealed class UnusedClassAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UnusedClass };

    public bool NeedsDependencyAnalysis => true;

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();
        var protectedByParent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var node in context.Models)
        {
            if (node.IsParseFailurePlaceholder || node.IsPartial || node.ClassType == "package")
                continue;
            if (!node.IsNested || string.IsNullOrEmpty(node.ParentModelName))
                continue;
            if (node.UsedByModelIds.Count > 0)
                continue;

            var protectedNames = ProtectedChildNames(context.Graph, node.ParentModelName, protectedByParent);
            if (protectedNames.Contains(node.Definition.Name))
                findings.Add(new Finding
                {
                    RuleId = RuleIdsRef.UnusedClass,
                    ModelId = node.Id,
                    Message = $"protected class {node.Definition.Name} is never used",
                    LineNumber = node.StartLine
                });
        }

        return findings;
    }

    // The simple names of the parent's protected nested classes (protected classes are declared inline
    // in the parent, so its stored code contains them even when it is a formatting shell). Cached per parent.
    private static HashSet<string> ProtectedChildNames(
        DirectedGraph graph, string parentId, Dictionary<string, HashSet<string>> cache)
    {
        if (cache.TryGetValue(parentId, out var cached))
            return cached;

        var result = new HashSet<string>(StringComparer.Ordinal);
        var code = graph.GetNode<ModelNode>(parentId)?.Definition?.ModelicaCode;
        if (!string.IsNullOrEmpty(code))
        {
            try
            {
                var iface = ClassInterfaceExtractor.ExtractFromCode(code);
                foreach (var element in iface.Elements)
                    if (element.Kind == ClassElementKind.Class && !element.IsPublic)
                        result.Add(element.Name);
            }
            catch
            {
                // Unparseable parent → treat as no known protected classes (under-report).
            }
        }

        cache[parentId] = result;
        return result;
    }
}
