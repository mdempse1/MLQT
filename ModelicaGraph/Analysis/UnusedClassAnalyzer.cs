using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags nested classes that nothing references (empty <c>UsedByModelIds</c>), in two confidence tiers:
/// <list type="bullet">
/// <item><b><c>MLQT.Unused.Class</c></b> — a <b>protected</b> nested class. Being protected it cannot be
/// used from outside its enclosing class, so this is high-confidence with no external-visibility false
/// positives.</item>
/// <item><b><c>MLQT.Unused.PublicClass</c></b> — a <b>public</b> nested class. Lower confidence (Info by
/// default): a downstream library we cannot see may use it, so this is only meaningful on an application
/// library, not a foundational one. Opt-in and off by default.</item>
/// </list>
/// Needs dependency analysis (the reverse edges are analyzer-only). Top-level classes are never flagged
/// (a library root is external API by definition); partial classes (extended, not instantiated) and
/// packages are skipped. Each tier only runs when its own rule is enabled.
/// </summary>
public sealed class UnusedClassAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UnusedClass, RuleIdsRef.UnusedPublicClass };

    public bool NeedsDependencyAnalysis => true;

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();
        var checkProtected = context.Settings.SeverityFor(RuleIdsRef.UnusedClass) != RuleSeverity.Off;
        var checkPublic = context.Settings.SeverityFor(RuleIdsRef.UnusedPublicClass) != RuleSeverity.Off;
        if (!checkProtected && !checkPublic)
            return findings;

        var childVisibilityByParent = new Dictionary<string, ChildClasses>(StringComparer.Ordinal);

        foreach (var node in context.Models)
        {
            if (node.IsParseFailurePlaceholder || node.IsPartial || node.ClassType == "package")
                continue;
            if (!node.IsNested || string.IsNullOrEmpty(node.ParentModelName))
                continue;
            if (node.UsedByModelIds.Count > 0)
                continue;

            var children = ChildClassNames(context.Graph, node.ParentModelName, childVisibilityByParent);
            if (checkProtected && children.Protected.Contains(node.Definition.Name))
                findings.Add(new Finding
                {
                    RuleId = RuleIdsRef.UnusedClass,
                    ModelId = node.Id,
                    Message = $"protected class {node.Definition.Name} is never used",
                    LineNumber = node.StartLine
                });
            else if (checkPublic && children.Public.Contains(node.Definition.Name))
                findings.Add(new Finding
                {
                    RuleId = RuleIdsRef.UnusedPublicClass,
                    ModelId = node.Id,
                    Message = $"public class {node.Definition.Name} may be unused — nothing in the loaded libraries references it",
                    LineNumber = node.StartLine
                });
        }

        return findings;
    }

    private readonly record struct ChildClasses(HashSet<string> Public, HashSet<string> Protected);

    // The simple names of the parent's nested classes split by visibility (nested classes are declared
    // inline in the parent, so its stored code contains them even when it is a formatting shell). Cached
    // per parent.
    private static ChildClasses ChildClassNames(
        DirectedGraph graph, string parentId, Dictionary<string, ChildClasses> cache)
    {
        if (cache.TryGetValue(parentId, out var cached))
            return cached;

        var publicNames = new HashSet<string>(StringComparer.Ordinal);
        var protectedNames = new HashSet<string>(StringComparer.Ordinal);
        var code = graph.GetNode<ModelNode>(parentId)?.Definition?.ModelicaCode;
        if (!string.IsNullOrEmpty(code))
        {
            try
            {
                var iface = ClassInterfaceExtractor.ExtractFromCode(code);
                foreach (var element in iface.Elements)
                    if (element.Kind == ClassElementKind.Class)
                        (element.IsPublic ? publicNames : protectedNames).Add(element.Name);
            }
            catch
            {
                // Unparseable parent → treat as no known nested classes (under-report).
            }
        }

        var result = new ChildClasses(publicNames, protectedNames);
        cache[parentId] = result;
        return result;
    }
}
