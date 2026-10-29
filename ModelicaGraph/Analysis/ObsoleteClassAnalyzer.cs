using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// <c>MLQT.Reference.Obsolete</c>: a class used where its library has marked it
/// <c>annotation(obsolete="...")</c>, the language's own way of saying "stop using this" (MLS §18.1).
/// </summary>
/// <remarks>
/// <para><b>What is obsolete.</b> A class carrying the annotation, and every class inside one: an
/// obsolete package's contents go with it. The annotation is not inherited - a class extending an
/// obsolete one is reported for the extends clause, not made obsolete by it.</para>
///
/// <para><b>What is a use.</b> Whatever names the class: a component's type, a base, a function
/// called, an import, a constant read through it. Found from the dependency edges, so a class with
/// none is passed over without its text being read, and then located in the class's own text with
/// <see cref="ReferenceLocator"/>, which resolves names exactly as dependency analysis does. Each
/// obsolete class is reported once per class using it, at its first use.</para>
///
/// <para><b>Not reported.</b> A use from a class that is itself obsolete, or inside one: obsolete
/// classes use each other, and a library's own obsolete package is not the user of it. Nor a class
/// MLQT cannot read - an encrypted one shows no annotations, so it is never known to be obsolete. That
/// can only under-report: a class is reported only when MLQT has read the annotation that says so.</para>
/// </remarks>
public sealed class ObsoleteClassAnalyzer : IGraphAnalyzer
{
    /// <inheritdoc/>
    public string Name => "ObsoleteClassAnalyzer";

    public IReadOnlyList<string> RuleIds { get; } = [RuleIdsRef.ObsoleteClass];

    public bool NeedsDependencyAnalysis => true;

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        if (context.Settings.SeverityFor(RuleIdsRef.ObsoleteClass) == RuleSeverity.Off)
            return [];

        var graph = context.Graph;
        var obsolete = new Obsolescence(graph);
        var shared = new ReferenceLocator.Shared();
        var findings = new List<Finding>();

        foreach (var model in context.Models)
        {
            if (model.IsExternalStub || model.IsParseFailurePlaceholder || obsolete.MarkOf(model.Id) is not null)
                continue;

            var used = model.UsedModelIds.Where(id => obsolete.MarkOf(id) is not null).ToList();
            if (used.Count == 0)
                continue;

            try
            {
                findings.AddRange(UsesIn(graph, model, used, obsolete, shared));
            }
            catch
            {
                // A class that cannot be analysed is skipped rather than breaking the run, as the
                // other analyses do.
            }
        }

        return findings;
    }

    private static IEnumerable<Finding> UsesIn(
        DirectedGraph graph, ModelNode model, List<string> used, Obsolescence obsolete, ReferenceLocator.Shared shared)
    {
        var sites = model.Definition.Borrow(
            tree =>
            {
                var locator = new ReferenceLocator(graph, used, shared)
                {
                    Within = ModelicaName.EnclosingPackageOf(model.Id),
                };
                locator.Visit(tree);
                return locator.Sites;
            },
            []);

        // The class's own references: a nested class written inline has a node of its own, and is
        // checked as itself.
        return sites
            .Where(site => string.Equals(site.ScopeId, model.Id, StringComparison.Ordinal))
            .GroupBy(site => site.TargetId, StringComparer.Ordinal)
            .Select(group => group.MinBy(site => site.Line)!)
            .OrderBy(site => site.Line)
            .Select(site => obsolete.MarkOf(site.TargetId) is { } mark
                ? new Finding
                {
                    RuleId = RuleIdsRef.ObsoleteClass,
                    ModelId = model.Id,
                    ElementPath = site.TargetId,
                    Message = MessageFor(site.TargetId, mark),
                    LineNumber = site.Line,
                }
                : null)
            .Where(f => f is not null)
            .Select(f => f!);
    }

    internal static string MessageFor(string usedId, Obsolescence.Mark mark)
    {
        var what = string.Equals(mark.MarkedId, usedId, StringComparison.Ordinal)
            ? $"{usedId} is obsolete"
            : $"{usedId} is inside {mark.MarkedId}, which is obsolete";
        return string.IsNullOrWhiteSpace(mark.Message) ? what : $"{what}: {mark.Message.Trim()}";
    }

    /// <summary>Whether a class is obsolete - marked itself, or inside a marked class - and by what.</summary>
    internal sealed class Obsolescence(DirectedGraph graph)
    {
        internal sealed record Mark(string MarkedId, string Message);

        private readonly ConcurrentDictionary<string, Mark?> _marks = new(StringComparer.Ordinal);

        /// <summary>The outermost obsolete class enclosing <paramref name="id"/>, itself included, or null.</summary>
        public Mark? MarkOf(string id) => _marks.GetOrAdd(id, Compute);

        private Mark? Compute(string id)
        {
            // The outermost wins: a package marked obsolete is what a reader should be told about,
            // whatever its contents say of themselves.
            if (ModelicaName.EnclosingPackageOf(id) is { Length: > 0 } parent && MarkOf(parent) is { } enclosing)
                return enclosing;

            return graph.GetNode<ModelNode>(id) is { ObsoleteMessage: { } message }
                ? new Mark(id, message)
                : null;
        }
    }
}
