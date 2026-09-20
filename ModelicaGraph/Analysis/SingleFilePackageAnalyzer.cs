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
/// <para><b>It reports the class that owns the file, once per file.</b> A package nested inside
/// another class's file has no file of its own and cannot be given a directory until its parent has
/// one, so a finding about it is advice nobody can take — and the fix offered on it wrote the
/// package somewhere else entirely and deleted the file it came from (B243). On the Modelica
/// Standard Library the difference is 302 findings over 71 files against 65, one per file.</para>
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
            var splittable = SplittableChildren(package, childrenByParent, context.Graph.GetNode<ModelNode>);
            if (splittable.Count == 0)
                continue;

            findings.Add(new Finding
            {
                RuleId = ModelicaParser.StyleRules.RuleIds.SingleFilePackage,
                ModelId = package.Id,
                ElementPath = package.Definition.Name,
                Message = splittable.Count == 1
                    ? $"package {package.Definition.Name} keeps '{splittable[0].Definition.Name}' inline; it could be stored in its own file"
                    : $"package {package.Definition.Name} keeps {splittable.Count} classes inline; they could each be stored in their own file",
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
        ModelNode package, IReadOnlyDictionary<string, List<ModelNode>> childrenByParent,
        Func<string, ModelNode?> byId)
    {
        // A class whose source could not be read at all is not something to report on, and it must
        // not take the analysis down with it: this rule runs in every check because it is on by
        // default, so one broken node would cost the whole library its findings rather than costing
        // that class its own (B244).
        if (package.ClassType != "package" || package.Definition is null)
            return [];

        // Only the class that owns the file (B243). A package nested inside another class's file is
        // not "stored as a single file" — it has no file — and it cannot be given a directory
        // without its parent becoming one first, so a finding about it is advice nobody can take.
        //
        // Reporting them was also 302 findings over 71 files on the Modelica Standard Library:
        // Spice3.mo alone produced 23, one for every package nested in it, for the one thing that is
        // actually true of it. One file, one finding.
        if (!OwnsItsFile(package, byId))
            return [];

        if (!childrenByParent.TryGetValue(package.Id, out var children) || children.Count == 0)
            return [];

        // A class that cannot be stored standalone — replaceable, redeclare, inner, outer — has to
        // live in its parent's file whatever the repository's convention, and so does one whose name
        // is not unique among its siblings once case is ignored: two of them cannot both be a file
        // on a case-insensitive filesystem. Same rule ModelicaPackageSaver writes by.
        var nameCounts = children
            .GroupBy(c => c.Definition.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        // Every child that could have a file and does not — <b>not</b> "all of them, or none"
        // (B243). That was the first reading, and a real library shows how wrong it is: MSL's
        // Spice3.Internal has thirteen classes in their own files and eight more, JFET among them,
        // still written into package.mo. A package half-way through being split is the ordinary
        // shape of this drift, not an exception to it, and saying nothing about it is how a class
        // stays put for years. The comparison is on the file, not the path: two classes in the same
        // file share a file id.
        return children
            .Where(c => c.CanBeStoredStandalone)
            .Where(c => nameCounts[c.Definition.Name.ToLowerInvariant()] == 1)
            .Where(c => !string.Equals(c.Definition.Name, "package", StringComparison.OrdinalIgnoreCase))
            .Where(c => SharesFileWith(c, package))
            .ToList();
    }

    /// <summary>
    /// Whether this class is the topmost one stored in its file — the one the file is "for".
    ///
    /// <para>The same question <c>IncrementalFormatter</c> asks to find a file's owner: its parent
    /// is nothing, or its parent lives somewhere else.</para>
    /// </summary>
    private static bool OwnsItsFile(ModelNode model, Func<string, ModelNode?> byId)
    {
        if (string.IsNullOrEmpty(model.ParentModelName))
            return true;

        var parent = byId(model.ParentModelName);
        return parent is null
            || !string.Equals(parent.ContainingFileId, model.ContainingFileId, StringComparison.Ordinal);
    }

    /// <summary>Every model's direct children, keyed by parent id — the index the check needs.</summary>
    public static Dictionary<string, List<ModelNode>> ChildrenByParent(DirectedGraph graph) =>
        graph.ModelNodes
            .Where(m => m is not null && m.Definition is not null
                     && !m.IsParseFailurePlaceholder && !string.IsNullOrEmpty(m.ParentModelName))
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
