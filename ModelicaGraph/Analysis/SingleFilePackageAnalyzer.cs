using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Reports a package held entirely in one <c>.mo</c> file when its classes could each have a file of
/// their own — the structural choice <b>Format All Files</b> undoes, and which until now MLQT could
/// only act on and never mention (B177).
///
/// <para>Whether a library is stored one-class-per-file is a repository's decision and not a defect,
/// which is why this is a rule with a severity rather than a hard-coded warning: some repositories
/// want it, some have libraries they deliberately keep single-file, and a few have both.</para>
///
/// <para><b>It is on by default</b>, unlike every other rule (B241). A library drifts into this
/// state without anyone doing anything — another tool saves a new package as one file, and the
/// incremental formatter rewrites it in place because it never moves a class between files — so a
/// user who has not heard of the rule is exactly the user who needs it.</para>
///
/// <para><b>The judgement is "could this be split", not "is this big".</b> A class that cannot be
/// stored standalone — <c>replaceable</c>, <c>redeclare</c>, <c>inner</c>, <c>outer</c> — has to live
/// inside its parent's file, so a package made only of those is correctly a single file and must not
/// be reported. Reporting it would be advice nobody can act on, which is the failure mode the
/// prerequisite mechanism exists to prevent elsewhere.</para>
///
/// <para>Structural only, so it needs no dependency analysis.</para>
/// </summary>
public sealed class SingleFilePackageAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } =
        new[] { ModelicaParser.StyleRules.RuleIds.SingleFilePackage };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();

        // From the whole graph, so a child already split into its own file is seen wherever that is.
        var childrenByParent = ChildrenByParent(context.Graph);

        foreach (var package in context.Models)
        {
            var splittable = SplittableChildren(package, childrenByParent);
            if (splittable.Count == 0)
                continue;

            findings.Add(new Finding
            {
                RuleId = ModelicaParser.StyleRules.RuleIds.SingleFilePackage,
                ModelId = package.Id,
                ElementPath = package.Definition.Name,
                Message = splittable.Count == 1
                    ? $"package {package.Definition.Name} is stored as a single file; its class '{splittable[0].Definition.Name}' could have a file of its own"
                    : $"package {package.Definition.Name} is stored as a single file; its {splittable.Count} classes could each have a file of their own",
                // The package as a whole. Finding lines are class-relative (see Finding.LineNumber),
                // and the thing being reported is where the file boundary is, not a line in it.
                LineNumber = 1
            });
        }

        return findings;
    }

    /// <summary>
    /// The classes this package holds inline that could each have a file of their own — the whole of
    /// what the rule reports, and the whole of what splitting it moves.
    ///
    /// <para><b>One answer, asked by both.</b> The analyzer decides whether to report and
    /// <c>PackageSplitter</c> decides what to write, and a fix that acted on a different set from
    /// the finding that offered it would be the "two copies of one decision" defect this repository
    /// keeps producing. Empty means there is nothing to report and nothing to do.</para>
    /// </summary>
    public static IReadOnlyList<ModelNode> SplittableChildren(
        ModelNode package, IReadOnlyDictionary<string, List<ModelNode>> childrenByParent)
    {
        if (package.ClassType != "package")
            return [];

        if (!childrenByParent.TryGetValue(package.Id, out var children) || children.Count == 0)
            return [];

        // Nothing to split: every child has to be inline whatever the repository's convention —
        // replaceable, redeclare, inner and outer classes cannot be pulled out into a file of their
        // own, so a package made only of those is correctly a single file.
        var splittable = children.Where(c => c.CanBeStoredStandalone).ToList();
        if (splittable.Count == 0)
            return [];

        // "Entirely in one file" — a package that has already been split, even partly, is a
        // different situation and neither the rule's business nor the fix's. The comparison is on
        // the file, not on the path: two classes in the same file share a file id.
        return splittable.Any(c => !SharesFileWith(c, package)) ? [] : splittable;
    }

    /// <summary>Every model's direct children, keyed by parent id — the index the check needs.</summary>
    public static Dictionary<string, List<ModelNode>> ChildrenByParent(DirectedGraph graph) =>
        graph.ModelNodes
            .Where(m => m is not null && !m.IsParseFailurePlaceholder && !string.IsNullOrEmpty(m.ParentModelName))
            .GroupBy(m => m.ParentModelName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

    /// <summary>
    /// True when the child is held in the package's own file. A child with no file id at all is
    /// treated as inline: it came from the package's source rather than from a file of its own.
    /// </summary>
    private static bool SharesFileWith(ModelNode child, ModelNode package) =>
        string.IsNullOrEmpty(child.ContainingFileId)
        || string.Equals(child.ContainingFileId, package.ContainingFileId, StringComparison.Ordinal);
}
