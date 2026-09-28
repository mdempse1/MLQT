using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Checks each package's <c>package.order</c> against what the package actually contains:
/// <list type="bullet">
/// <item><b>stale</b> — an entry that names no class or member of the package;</item>
/// <item><b>missing</b> — a direct child class that is not listed.</item>
/// </list>
/// Needs no dependency analysis (purely structural). Valid entries are the package's child classes
/// unioned with its package-level members (constants/variables), so a legitimately-listed constant is
/// never flagged as stale; imports/extends are not package.order entries and are ignored. If a
/// package's own source cannot be read for its members, stale checks are skipped for it (under-report
/// rather than risk a false positive); missing-class checks always run from the graph.
/// </summary>
public sealed class PackageOrderAnalyzer : IGraphAnalyzer
{
    public IReadOnlyList<string> RuleIds { get; } = new[] { ModelicaParser.StyleRules.RuleIds.PackageOrder };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var findings = new List<Finding>();
        var matchDymola = context.Settings.PackageOrderMatchesDymola;

        // parent id -> direct child class simple names (from the whole graph, so it spans every file).
        var childClasses = context.Graph.ModelNodes
            .Where(m => m is not null && !m.IsParseFailurePlaceholder && !string.IsNullOrEmpty(m.ParentModelName))
            .GroupBy(m => m.ParentModelName!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(m => m.Definition.Name).ToList(), StringComparer.Ordinal);

        foreach (var package in context.Models)
        {
            if (package.ClassType != "package" || package.PackageOrder is null)
                continue;

            var declared = package.PackageOrder.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (declared.Count == 0)
                continue;

            var children = childClasses.TryGetValue(package.Id, out var c) ? c : new List<string>();

            // Stale: an entry matching neither a child class nor a package-level member. Only run when
            // the package's members could be read (else a legitimate constant would look stale).
            // Stale entries have no upstream equivalent — Dymola's loader reports an *incomplete*
            // package.order and says nothing about an entry naming something that is not there — so
            // a repository asking for Dymola's answer is not asking for these.
            var members = matchDymola ? null : ExtractMemberNames(package);
            if (members is not null)
            {
                var valid = new HashSet<string>(children, StringComparer.Ordinal);
                valid.UnionWith(members);

                var reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in declared)
                    if (!valid.Contains(entry) && reported.Add(entry))
                        findings.Add(new Finding
                        {
                            RuleId = ModelicaParser.StyleRules.RuleIds.PackageOrder,
                            ModelId = package.Id,
                            ElementPath = entry,
                            Discriminator = "stale",
                            Message = $"package.order lists '{entry}', which is not a class or member of {package.Definition.Name}",
                            // The package as a whole — package.order is a separate file, and finding
                            // lines are class-relative (see Finding.LineNumber).
                            LineNumber = 1
                        });
            }

            // Missing: a direct child class not listed in package.order.
            var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
            foreach (var childName in children)
                if (!declaredSet.Contains(childName)
                    && (!matchDymola || DymolaWouldFind(context, package, childName)))
                    findings.Add(new Finding
                    {
                        RuleId = ModelicaParser.StyleRules.RuleIds.PackageOrder,
                        ModelId = package.Id,
                        ElementPath = childName,
                        Discriminator = "missing",
                        Message = $"class '{childName}' is not listed in the package.order of {package.Definition.Name}",
                        LineNumber = 1
                    });
        }

        return findings;
    }

    /// <summary>
    /// Whether Dymola's own loader would have found this child and warned about it (B195).
    ///
    /// <para>Dymola resolves a package's children in exactly two places:
    /// <c>&lt;Package&gt;/Name.mo</c> and <c>&lt;Package&gt;/Name/package.mo</c>. A class held inline
    /// in the package's own <c>package.mo</c> is found too, because Dymola is already reading that
    /// file.</para>
    ///
    /// <para><b>The difference this leaves is narrower than B195 assumed, and it was measured rather
    /// than reasoned.</b> The item expected MLQT to find classes in folders Dymola never descends
    /// into; it does not, because <c>LibraryDataService</c> skips a directory with no
    /// <c>package.mo</c> for the same reason Dymola does. What is left is a class in a file whose
    /// name does not match it — <c>Widget.mo</c> holding <c>model Odd</c>. MLQT reads every
    /// <c>.mo</c> in a package directory and takes the class's own name, so it loads that class and
    /// reports it unlisted; Dymola resolves by file name and cannot load it from there at all, so it
    /// never warns. Worth keeping in the default answer: a class stored where the tool of record
    /// cannot find it is a real problem, and this is the only thing that notices.</para>
    /// </summary>
    private static bool DymolaWouldFind(GraphAnalysisContext context, ModelNode package, string childName)
    {
        var packageFile = FilePathOf(context, package.ContainingFileId);
        if (packageFile is null)
            return true; // Cannot tell where it lives; report rather than silently drop.

        var child = context.Graph.ModelNodes.FirstOrDefault(
            m => string.Equals(m.ParentModelName, package.Id, StringComparison.Ordinal)
                 && string.Equals(m.Definition.Name, childName, StringComparison.Ordinal));

        var childFile = child is null ? null : FilePathOf(context, child.ContainingFileId);
        if (childFile is null || string.Equals(childFile, packageFile, StringComparison.OrdinalIgnoreCase))
            return true; // Inline in the package's own file, which Dymola is already reading.

        var packageDir = Path.GetDirectoryName(packageFile);
        if (string.IsNullOrEmpty(packageDir))
            return true;

        return PathsEqual(childFile, Path.Combine(packageDir, childName + ".mo"))
            || PathsEqual(childFile, Path.Combine(packageDir, childName, "package.mo"));
    }

    private static string? FilePathOf(GraphAnalysisContext context, string? fileId) =>
        string.IsNullOrEmpty(fileId) ? null : context.Graph.GetNode<FileNode>(fileId)?.FilePath;

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    // The package's package-level member (constant/variable) names — the entries package.order may
    // legitimately contain that are not child classes. Null if the source can't be parsed.
    private static HashSet<string>? ExtractMemberNames(ModelNode package)
    {
        var code = package.Definition.ModelicaCode;
        if (string.IsNullOrEmpty(code))
            return null;

        try
        {
            var iface = ClassInterfaceExtractor.ExtractFromCode(code);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in iface.Elements)
                if (element.Kind == ClassElementKind.Component)
                    names.Add(element.Name);
            return names;
        }
        catch
        {
            return null;
        }
    }
}
