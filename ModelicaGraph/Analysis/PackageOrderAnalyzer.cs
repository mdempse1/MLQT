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
            var members = ExtractMemberNames(package);
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
                            LineNumber = package.StartLine
                        });
            }

            // Missing: a direct child class not listed in package.order.
            var declaredSet = new HashSet<string>(declared, StringComparer.Ordinal);
            foreach (var childName in children)
                if (!declaredSet.Contains(childName))
                    findings.Add(new Finding
                    {
                        RuleId = ModelicaParser.StyleRules.RuleIds.PackageOrder,
                        ModelId = package.Id,
                        ElementPath = childName,
                        Discriminator = "missing",
                        Message = $"class '{childName}' is not listed in the package.order of {package.Definition.Name}",
                        LineNumber = package.StartLine
                    });
        }

        return findings;
    }

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
