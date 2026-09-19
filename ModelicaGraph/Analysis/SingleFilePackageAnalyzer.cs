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
/// want it, some have libraries they deliberately keep single-file, and a few have both. It is off
/// by default like every other rule.</para>
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

        // parent id -> its direct children, from the whole graph so a child already split into its
        // own file is seen wherever that file is.
        var childrenByParent = context.Graph.ModelNodes
            .Where(m => m is not null && !m.IsParseFailurePlaceholder && !string.IsNullOrEmpty(m.ParentModelName))
            .GroupBy(m => m.ParentModelName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        foreach (var package in context.Models)
        {
            if (package.ClassType != "package")
                continue;

            if (!childrenByParent.TryGetValue(package.Id, out var children) || children.Count == 0)
                continue;

            // Nothing to split: every child has to be inline whatever the repository's convention.
            var splittable = children.Where(c => c.CanBeStoredStandalone).ToList();
            if (splittable.Count == 0)
                continue;

            // "Entirely in one file" — a package that has already been split, even partly, is a
            // different situation and not this rule's business. The comparison is on the file, not on
            // the path: two classes in the same file share a file id.
            if (splittable.Any(c => !SharesFileWith(c, package)))
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
    /// True when the child is held in the package's own file. A child with no file id at all is
    /// treated as inline: it came from the package's source rather than from a file of its own.
    /// </summary>
    private static bool SharesFileWith(ModelNode child, ModelNode package) =>
        string.IsNullOrEmpty(child.ContainingFileId)
        || string.Equals(child.ContainingFileId, package.ContainingFileId, StringComparison.Ordinal);
}
