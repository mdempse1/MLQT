using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// <c>MLQT.Structure.DependencyCycle</c>: packages that depend on each other. <c>Components</c> uses
/// <c>Interfaces</c> and <c>Interfaces</c> uses <c>Components</c>, so neither can be understood,
/// tested or moved on its own.
/// </summary>
/// <remarks>
/// <para><b>Packages, not classes.</b> Classes that refer to each other are ordinary Modelica - a
/// function and the record it returns, a model and the connector it declares - and reporting them
/// would be noise. What a cycle costs is a layering one, so each class-to-class dependency is lifted
/// to the one level where the two names part: <c>Lib.Components.Source</c> using
/// <c>Lib.Interfaces.Pin</c> is a dependency of <c>Lib.Components</c> on <c>Lib.Interfaces</c>, among
/// the children of <c>Lib</c>. A class using something in its own enclosing scope - a constant of its
/// package, the package itself - is not a dependency between packages and counts for nothing.</para>
///
/// <para><b>Once, at that level.</b> Because each dependency counts only where the two names part,
/// a cycle between two classes deep inside <c>Lib.Blocks</c> is reported among <c>Lib.Blocks</c>'
/// children and not again among <c>Lib</c>'s. The cycles among one package's children are its
/// strongly connected components, each reported once against that package, with one concrete
/// cycle through it and the class that makes each step, which is what a reader needs to break it.
/// Distinct libraries depending on each other are the same question one level up.</para>
///
/// <para><b>Only the libraries being checked.</b> A dependency that leaves them is not followed: a
/// cycle through a library MLQT is not checking - a reference library, an excluded one - is not this
/// library's to report, and leaving it out can only under-report. Needs the dependency edges.</para>
/// </remarks>
public sealed class DependencyCycleAnalyzer : IGraphAnalyzer
{
    /// <inheritdoc/>
    public string Name => "DependencyCycleAnalyzer";

    public IReadOnlyList<string> RuleIds { get; } = [RuleIdsRef.DependencyCycle];

    public bool NeedsDependencyAnalysis => true;

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        if (context.Settings.SeverityFor(RuleIdsRef.DependencyCycle) == RuleSeverity.Off)
            return [];

        var checkedRoots = context.Models
            .Select(m => ModelicaName.RootLibraryOf(m.Id))
            .ToHashSet(StringComparer.Ordinal);

        var groups = PackageDependencies(context.Graph, checkedRoots);

        var findings = new List<Finding>();
        foreach (var (parent, group) in groups.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            foreach (var component in group.CyclicComponents())
            {
                var cycle = group.ShortestCycleThrough(component[0], component);
                findings.Add(new Finding
                {
                    RuleId = RuleIdsRef.DependencyCycle,
                    ModelId = OwnerOf(context.Graph, parent, component),
                    // Which packages are in it, which is what the cycle *is*: a different route
                    // through the same packages is the same cycle, so the route is not part of it.
                    ElementPath = string.Join(", ", component),
                    Message = MessageFor(parent, component, cycle, group),
                    LineNumber = 1,
                });
            }
        }

        return findings;
    }

    /// <summary>
    /// Every class-to-class dependency inside the checked libraries, lifted to the packages where the
    /// two names part, grouped by the package whose children they are ("" for distinct libraries).
    /// </summary>
    internal static Dictionary<string, SiblingGraph> PackageDependencies(DirectedGraph graph, IReadOnlySet<string> checkedRoots)
    {
        var groups = new Dictionary<string, SiblingGraph>(StringComparer.Ordinal);

        foreach (var model in graph.ModelNodes)
        {
            if (model.IsParseFailurePlaceholder || model.IsExternalStub
                || !checkedRoots.Contains(ModelicaName.RootLibraryOf(model.Id)))
                continue;

            // An example is an entry point that nothing depends on, so what it uses costs nobody
            // anything; its whole job is to use parts from across the library. Counted, MSL's
            // Blocks.Examples building circuits from Electrical and Mechanics put most of MSL's
            // top-level packages in one cycle. The same exemption MLQT.Unused.Class makes.
            if (model.HasExperimentAnnotation)
                continue;

            var from = ModelicaName.Segments(model.Id);
            foreach (var usedId in model.UsedModelIds)
            {
                var used = graph.GetNode<ModelNode>(usedId);
                if (used is null || used.IsExternalStub || !checkedRoots.Contains(ModelicaName.RootLibraryOf(usedId)))
                    continue;

                var to = ModelicaName.Segments(usedId);
                var shared = 0;
                while (shared < from.Count && shared < to.Count
                       && string.Equals(from[shared], to[shared], StringComparison.Ordinal))
                    shared++;

                // One is inside the other: a class and its own enclosing scope.
                if (shared == from.Count || shared == to.Count)
                    continue;

                // Packages on both sides, at the level where the names part. Where that is two
                // classes - inverse functions naming each other, a model and the record it uses -
                // they are classes referring to each other, which is ordinary Modelica.
                var fromPackage = string.Join(".", from.Take(shared + 1));
                var toPackage = string.Join(".", to.Take(shared + 1));
                if (!IsPackage(graph, fromPackage) || !IsPackage(graph, toPackage))
                    continue;

                var parent = string.Join(".", from.Take(shared));
                if (!groups.TryGetValue(parent, out var group))
                    groups[parent] = group = new SiblingGraph();

                group.Add(fromPackage, toPackage, model.Id, usedId);
            }
        }

        return groups;
    }

    private static bool IsPackage(DirectedGraph graph, string id) =>
        graph.GetNode<ModelNode>(id) is { ClassType: "package" };

    /// <summary>
    /// The class a cycle is reported on: the package whose children are in it, or for libraries that
    /// depend on each other - which have no common package - the first of them.
    /// </summary>
    private static string OwnerOf(DirectedGraph graph, string parent, IReadOnlyList<string> component) =>
        parent.Length > 0 && graph.GetNode<ModelNode>(parent) is not null ? parent : component[0];

    private static string MessageFor(string parent, IReadOnlyList<string> component, IReadOnlyList<string> cycle, SiblingGraph group)
    {
        var shortNames = component.Select(c => ShortName(parent, c)).ToList();
        var what = parent.Length == 0 ? "libraries" : "packages";
        var who = shortNames.Count == 2
            ? $"{shortNames[0]} and {shortNames[1]}"
            : string.Join(", ", shortNames.Take(shortNames.Count - 1)) + " and " + shortNames[^1];

        var steps = new List<string>();
        for (var i = 0; i < cycle.Count; i++)
        {
            var (user, used) = group.ExampleOf(cycle[i], cycle[(i + 1) % cycle.Count]);
            steps.Add($"{user} uses {used}");
        }

        return $"The {what} {who} depend on each other: {string.Join("; ", steps)}";
    }

    private static string ShortName(string parent, string member) =>
        parent.Length == 0 ? member : member[(parent.Length + 1)..];

    /// <summary>
    /// The dependencies among one package's children, with one class-level example for each.
    /// </summary>
    internal sealed class SiblingGraph
    {
        private readonly Dictionary<string, SortedSet<string>> _edges = new(StringComparer.Ordinal);
        private readonly Dictionary<(string From, string To), (string User, string Used)> _examples = new();

        public void Add(string from, string to, string user, string used)
        {
            if (!_edges.TryGetValue(from, out var targets))
                _edges[from] = targets = new SortedSet<string>(StringComparer.Ordinal);
            targets.Add(to);
            if (!_edges.ContainsKey(to))
                _edges[to] = new SortedSet<string>(StringComparer.Ordinal);

            // The first example by name, so the message does not change from run to run.
            if (!_examples.TryGetValue((from, to), out var kept)
                || string.CompareOrdinal(user + " " + used, kept.User + " " + kept.Used) < 0)
                _examples[(from, to)] = (user, used);
        }

        public (string User, string Used) ExampleOf(string from, string to) => _examples[(from, to)];

        /// <summary>
        /// The strongly connected components with more than one member, each sorted, in order of
        /// their first member (Tarjan's algorithm, iteratively, so a deep chain cannot overflow).
        /// </summary>
        public List<List<string>> CyclicComponents()
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var low = new Dictionary<string, int>(StringComparer.Ordinal);
            var onStack = new HashSet<string>(StringComparer.Ordinal);
            var stack = new Stack<string>();
            var result = new List<List<string>>();
            var next = 0;

            foreach (var start in _edges.Keys.Order(StringComparer.Ordinal))
            {
                if (index.ContainsKey(start))
                    continue;

                var work = new Stack<(string Node, IEnumerator<string> Targets)>();
                index[start] = low[start] = next++;
                stack.Push(start);
                onStack.Add(start);
                work.Push((start, _edges[start].GetEnumerator()));

                while (work.Count > 0)
                {
                    var (node, targets) = work.Peek();
                    if (targets.MoveNext())
                    {
                        var target = targets.Current;
                        if (!index.ContainsKey(target))
                        {
                            index[target] = low[target] = next++;
                            stack.Push(target);
                            onStack.Add(target);
                            work.Push((target, _edges[target].GetEnumerator()));
                        }
                        else if (onStack.Contains(target))
                        {
                            low[node] = Math.Min(low[node], index[target]);
                        }
                        continue;
                    }

                    work.Pop();
                    if (work.Count > 0)
                        low[work.Peek().Node] = Math.Min(low[work.Peek().Node], low[node]);

                    if (low[node] != index[node])
                        continue;

                    var component = new List<string>();
                    string member;
                    do
                    {
                        member = stack.Pop();
                        onStack.Remove(member);
                        component.Add(member);
                    } while (member != node);

                    if (component.Count > 1)
                        result.Add(component.Order(StringComparer.Ordinal).ToList());
                }
            }

            return result.OrderBy(c => c[0], StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The shortest cycle from <paramref name="start"/> back to itself inside one component,
        /// breadth first, so the route the message names is as short as the cycle allows.
        /// </summary>
        public List<string> ShortestCycleThrough(string start, IReadOnlyCollection<string> component)
        {
            var inComponent = component.ToHashSet(StringComparer.Ordinal);
            var cameFrom = new Dictionary<string, string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var target in _edges[node])
                {
                    if (!inComponent.Contains(target))
                        continue;

                    if (target == start)
                    {
                        var path = new List<string> { node };
                        while (path[^1] != start)
                            path.Add(cameFrom[path[^1]]);
                        path.Reverse();
                        return path;
                    }

                    if (cameFrom.TryAdd(target, node))
                        queue.Enqueue(target);
                }
            }

            return [start];   // not reached for a member of a component of two or more
        }
    }
}
