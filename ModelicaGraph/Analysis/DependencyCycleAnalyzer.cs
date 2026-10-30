using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// <c>MLQT.Structure.DependencyCycle</c>: top-level libraries that depend on each other. <c>LibA</c>
/// uses <c>LibB</c> and <c>LibB</c> uses <c>LibA</c>, so neither can be loaded, versioned or released
/// without the other.
/// </summary>
/// <remarks>
/// <para><b>Libraries, not their sub-libraries.</b> Only a dependency between two top-level
/// libraries counts. Packages inside one library depending on each other - <c>Interfaces</c> using
/// <c>Components</c> - are that library's own business: they are loaded, versioned and released
/// together, so a cycle between them costs nothing a user of the library can see. The first version
/// lifted every dependency to the level where the two names part and reported 24 such cycles in
/// MSL, none of which anybody would act on. A top-level class that is not a package (a lone model
/// file) is not a library, and a class using its own library counts for nothing.</para>
///
/// <para><b>Reported on each checked library in the cycle.</b> Dependencies are followed across
/// every library loaded, and a cycle is reported once on each checked library in it. Reported once
/// "on the first", a surface checking one library at a time (the MCP server) could never see it while
/// one checking a repository (the desktop app) did, so the same libraries gave different findings by
/// tool. A library that is loaded but not checked is followed and never reported on. The cycles are
/// the strongly connected components of the libraries' dependencies, each with one concrete route
/// round it and the class that makes each step, which is what a reader needs to break it. Needs the
/// dependency edges.</para>
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

        var libraries = LibraryDependencies(context.Graph);

        var findings = new List<Finding>();
        foreach (var component in libraries.CyclicComponents())
        {
            // On each checked library in it; a cycle of libraries none of which is checked is
            // nobody's to report.
            var owners = component.Where(checkedRoots.Contains).ToList();
            if (owners.Count == 0)
                continue;

            var cycle = libraries.ShortestCycleThrough(component[0], component);
            foreach (var owner in owners)
            {
                findings.Add(new Finding
                {
                    RuleId = RuleIdsRef.DependencyCycle,
                    ModelId = owner,
                    // Which libraries are in it, which is what the cycle *is*: a different route
                    // through the same libraries is the same cycle, so the route is not part of it.
                    ElementPath = string.Join(", ", component),
                    Message = MessageFor(component, cycle, libraries),
                    LineNumber = 1,
                });
            }
        }

        return findings;
    }

    /// <summary>
    /// Every class-to-class dependency between two loaded top-level libraries, as a dependency of the
    /// one library on the other, whichever of them is being checked.
    /// </summary>
    internal static LibraryGraph LibraryDependencies(DirectedGraph graph)
    {
        var libraries = new LibraryGraph();

        foreach (var model in graph.ModelNodes)
        {
            if (model.IsParseFailurePlaceholder || model.IsExternalStub)
                continue;

            // An example is an entry point that nothing depends on, so what it uses costs nobody
            // anything; its whole job is to use parts from wherever they are. The same exemption
            // MLQT.Unused.Class makes.
            if (model.HasExperimentAnnotation)
                continue;

            var fromLibrary = ModelicaName.RootLibraryOf(model.Id);
            foreach (var usedId in model.UsedModelIds)
            {
                var used = graph.GetNode<ModelNode>(usedId);
                if (used is null || used.IsExternalStub)
                    continue;

                // Within one library, however far apart: its sub-libraries are its own business.
                var toLibrary = ModelicaName.RootLibraryOf(usedId);
                if (string.Equals(fromLibrary, toLibrary, StringComparison.Ordinal))
                    continue;

                if (!IsPackage(graph, fromLibrary) || !IsPackage(graph, toLibrary))
                    continue;

                libraries.Add(fromLibrary, toLibrary, model.Id, usedId);
            }
        }

        return libraries;
    }

    private static bool IsPackage(DirectedGraph graph, string id) =>
        graph.GetNode<ModelNode>(id) is { ClassType: "package" };

    private static string MessageFor(IReadOnlyList<string> component, IReadOnlyList<string> cycle, LibraryGraph libraries)
    {
        var who = component.Count == 2
            ? $"{component[0]} and {component[1]}"
            : string.Join(", ", component.Take(component.Count - 1)) + " and " + component[^1];

        var steps = new List<string>();
        for (var i = 0; i < cycle.Count; i++)
        {
            var (user, used) = libraries.ExampleOf(cycle[i], cycle[(i + 1) % cycle.Count]);
            steps.Add($"{user} uses {used}");
        }

        return $"The libraries {who} depend on each other: {string.Join("; ", steps)}";
    }

    /// <summary>
    /// The dependencies between top-level libraries, with one class-level example for each.
    /// </summary>
    internal sealed class LibraryGraph
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
