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
/// declaration is unused. Uses the shared resolver to determine "is extended", so it needs no
/// dependency analysis — but when the edges are there it asks only the classes that use a candidate
/// rather than every class in the graph (B281).
/// </summary>
public sealed class UnusedMembersAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UnusedMember };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        // The classes pass 2 will look at, decided first so pass 1 can be asked only about them.
        var candidates = context.Models
            .Where(n => !n.IsParseFailurePlaceholder && !n.IsPartial && n.ClassType != "package")
            .ToList();
        if (candidates.Count == 0)
            return [];

        // Pass 1: which of them something extends - their members may be used by subclasses.
        var extended = ExtendedAmong(context, candidates);

        // Pass 2: check each leaf (un-extended) class with no nested classes. In parallel, and put back
        // in the order the classes were given, so the report does not depend on thread timing.
        var perClass = new List<Finding>?[candidates.Count];
        Parallel.For(0, candidates.Count, i =>
        {
            var node = candidates[i];
            if (!extended.Contains(node.Id))
                perClass[i] = UnusedIn(node);
        });

        return perClass.Where(f => f is not null).SelectMany(f => f!).ToList();
    }

    /// <summary>
    /// Which of <paramref name="candidates"/> some class extends.
    /// </summary>
    /// <remarks>
    /// <para><b>Asked of the classes that use a candidate, not of the whole graph (B281).</b> The
    /// question used to be answered by parsing every class in the graph for its extends clauses — 39,596
    /// on Claytex with a tool's library folder loaded, to report on 21,673 — and that pass was ~95% of
    /// this analyzer's 41 seconds, on one thread. A class that extends another uses it, so once
    /// dependency analysis has run the only classes that can extend a candidate are in its
    /// <c>UsedByModelIds</c>. They are still asked the same question with the same resolver: the edge
    /// decides who is asked, never the answer.</para>
    ///
    /// <para>That rests on dependency analysis recording every extends clause as an edge, and it was
    /// measured before being relied on: over the Claytex repository with the Dymola 2026x library folder,
    /// all 16,226 extends clauses into the checked set carried an edge, and the extended set came out
    /// the same 2,264 classes either way. Without the edges the whole graph is scanned, as it was.</para>
    /// </remarks>
    private static HashSet<string> ExtendedAmong(GraphAnalysisContext context, List<ModelNode> candidates)
    {
        IEnumerable<ModelNode> askers;
        if (context.DependenciesAnalyzed)
        {
            var users = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
                users.UnionWith(candidate.UsedByModelIds);
            askers = users
                .Select(id => context.Graph.GetNode<ModelNode>(id))
                .Where(n => n is not null)
                .Cast<ModelNode>()
                .ToList();
        }
        else
        {
            askers = context.Graph.ModelNodes.ToList();
        }

        var extended = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.Ordinal);
        Parallel.ForEach(askers, node =>
        {
            if (node is null || node.IsParseFailurePlaceholder)
                return;
            try
            {
                // Borrowed, not taken: TypeResolver below reaches for base classes, and a tree this
                // pass did not parse belongs to whoever did. See ModelDefinition.Borrow.
                var iface = node.Definition.Borrow<ClassInterface?>(ClassInterfaceExtractor.Extract);
                if (iface is null)
                    return;
                var extendsClauses = iface.Elements.Where(e => e.Kind == ClassElementKind.Extends).ToList();
                if (extendsClauses.Count == 0)
                    return;
                var imports = iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList();
                foreach (var ext in extendsClauses)
                {
                    var baseNode = TypeResolver.Resolve(context.Graph, node.Id, ext.Type, imports);
                    if (baseNode is not null)
                        extended.TryAdd(baseNode.Id, 0);
                }
            }
            catch { }
        });

        return extended.Keys.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The protected members of one class that nothing in it references, or null for none.</summary>
    private static List<Finding>? UnusedIn(ModelNode node)
    {
        List<Finding>? findings = null;
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
                        (findings ??= []).Add(new Finding
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
