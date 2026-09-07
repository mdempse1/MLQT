using Antlr4.Runtime.Tree;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Visitors;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags a <b>protected</b> component/parameter/constant that is never referenced in its class. Scoped
/// carefully to avoid false positives: a protected member is only visible within its class (and its
/// nested classes and subclasses), so this only checks a class that <b>nothing extends</b> (a member
/// used only by a subclass would otherwise look unused) and that has <b>no nested classes</b> (which
/// could reference it lexically). Within such a class a protected name that appears only at its own
/// declaration is unused. Uses the shared resolver to determine "is extended" by parsing, so it needs
/// no dependency analysis.
/// </summary>
public sealed class UnusedMembersAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UnusedMember };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();

        // Pass 1: the set of classes that something extends — their members may be used by subclasses.
        var extended = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in context.Graph.ModelNodes)
        {
            if (node is null || node.IsParseFailurePlaceholder)
                continue;
            try
            {
                // Borrowed, not taken: TypeResolver below reaches for base classes, and a tree this
                // pass did not parse belongs to whoever did. See ModelDefinition.Borrow.
                var iface = node.Definition.Borrow<ClassInterface?>(ClassInterfaceExtractor.Extract);
                if (iface is null)
                    continue;
                var extendsClauses = iface.Elements.Where(e => e.Kind == ClassElementKind.Extends).ToList();
                if (extendsClauses.Count == 0)
                    continue;
                var imports = iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList();
                foreach (var ext in extendsClauses)
                {
                    var baseNode = TypeResolver.Resolve(context.Graph, node.Id, ext.Type, imports);
                    if (baseNode is not null)
                        extended.Add(baseNode.Id);
                }
            }
            catch { }
        }

        // Pass 2: check each leaf (un-extended) class with no nested classes.
        foreach (var node in context.Models)
        {
            if (node.IsParseFailurePlaceholder || node.IsPartial || node.ClassType == "package")
                continue;
            if (extended.Contains(node.Id))
                continue;

            try
            {
                // The whole check runs inside the borrow, because it wants the tree itself and not
                // just the interface — CountIdentifiers walks every token. See ModelDefinition.Borrow.
                node.Definition.Borrow(tree =>
                {
                    var iface = ClassInterfaceExtractor.Extract(tree);
                    if (iface.Elements.Any(e => e.Kind == ClassElementKind.Class))
                        return;   // a nested class could reference a protected member lexically — don't guess

                    var protectedMembers = iface.Elements
                        .Where(e => e.Kind == ClassElementKind.Component && !e.IsPublic)
                        .ToList();
                    if (protectedMembers.Count == 0)
                        return;

                    var counts = CountIdentifiers(tree);
                    foreach (var member in protectedMembers)
                        if (counts.GetValueOrDefault(member.Name, 0) <= 1)   // only its own declaration
                            findings.Add(new Finding
                            {
                                RuleId = RuleIdsRef.UnusedMember,
                                ModelId = node.Id,
                                ElementPath = member.Name,
                                Message = $"protected {member.Name} is never used in {node.Definition.Name}",
                                LineNumber = member.Line
                            });
                });
            }
            catch { }
        }

        return findings;
    }

    // Occurrence count of every IDENT token in the class tree. A member used somewhere appears at least
    // twice (its declaration plus each use); one occurrence means declaration-only, i.e. unused.
    private static Dictionary<string, int> CountIdentifiers(IParseTree tree)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        Walk(tree);
        return counts;

        void Walk(IParseTree node)
        {
            if (node is ITerminalNode terminal && terminal.Symbol.Type == modelicaParser.IDENT)
            {
                var name = terminal.GetText();
                counts[name] = counts.GetValueOrDefault(name, 0) + 1;
            }
            for (var i = 0; i < node.ChildCount; i++)
                Walk(node.GetChild(i));
        }
    }
}
