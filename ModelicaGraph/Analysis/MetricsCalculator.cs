using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;

namespace ModelicaGraph.Analysis;

/// <summary>One coverage dimension: how many eligible elements comply, out of how many are eligible.</summary>
public sealed record CoverageMetric(string Dimension, int Compliant, int Eligible)
{
    /// <summary>Percentage compliant (100 when nothing is eligible), rounded to one decimal.</summary>
    public double Percent => Eligible == 0 ? 100.0 : Math.Round(100.0 * Compliant / Eligible, 1);
}

/// <summary>Aggregate metrics for a set of models: size counts and quality-coverage percentages.</summary>
/// <param name="MeasuredNow">How many of the classes had to be measured during this report rather
/// than having been measured already while they were checked. Zero is the intended state; a large
/// number is why a report was slow.</param>
public sealed record LibraryMetrics(
    int TotalClasses,
    IReadOnlyDictionary<string, int> ClassesByType,
    int TotalComponents,
    IReadOnlyList<CoverageMetric> Coverage,
    int MeasuredNow = 0);

/// <summary>
/// Computes the metrics-dashboard figures over a set of models: class counts by kind, total component
/// count, and quality-coverage percentages (description, icon, parameter description, unit). Coverage is
/// a dedicated pass over each class's structure — independent of which rules are enabled and of whether
/// style checking has run — so the dashboard is always available and shows the true state (waivers are
/// ignored: a suppressed gap still counts as a gap). Pure and side-effect-free (parse trees released).
/// </summary>
public static class MetricsCalculator
{
    public static LibraryMetrics Compute(DirectedGraph graph, IEnumerable<ModelNode> models)
    {
        // Stubs are excluded from every count and every coverage denominator. They stand for classes
        // in an encrypted third-party library, so their description and icon coverage is neither the
        // user's achievement nor the user's debt — folding a vendor's library into the numbers would
        // move the percentages for reasons the user cannot act on. Filtered here rather than in each
        // caller so no surface can drift from the others.
        var classes = models
            .Where(m => m is not null && !m.IsParseFailurePlaceholder && !m.IsExternalStub)
            .ToList();
        var total = classes.Count;
        var byType = classes
            .GroupBy(m => string.IsNullOrEmpty(m.ClassType) ? "unknown" : m.ClassType, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var measurer = new CoverageMeasurer(graph);

        int withDescription = 0, withIcon = 0, components = 0;
        int paramTotal = 0, paramWithDesc = 0;
        int realTotal = 0, realWithUnit = 0;

        foreach (var model in classes)
        {
            // Measured once per class and kept. The same class is measured for every scope the
            // dashboard asks about — all libraries, then one sub-package, then each of them side by
            // side — and measuring means parsing it and walking its interface again each time.
            var facts = measurer.Measure(model);
            if (facts is null)
                continue;

            if (facts.HasDescription) withDescription++;
            if (facts.HasIcon) withIcon++;
            components += facts.Components;
            paramTotal += facts.ParameterTotal;
            paramWithDesc += facts.ParametersWithDescription;
            realTotal += facts.RealTotal;
            realWithUnit += facts.RealWithUnit;
        }

        var coverage = new List<CoverageMetric>
        {
            new("Description", withDescription, total),
            new("Icon", withIcon, total),
            new("Parameter description", paramWithDesc, paramTotal),
            new("Unit", realWithUnit, realTotal),
        };

        return new LibraryMetrics(total, byType, components, coverage, measurer.MeasuredHere);
    }
}

/// <summary>
/// Measures what one class contributes to coverage, and remembers it on the class.
///
/// <para>Built once per pass because two of its inputs are worth sharing: the inherited-icon walk
/// memoises across classes, and the type resolver's verdicts are the same handful of SI types
/// answering for most components in a library.</para>
///
/// <para>Anything holding a parsed class can hand it here — style checking does, while the tree it
/// already needed is still in hand — so the dashboard finds the work done rather than doing it.</para>
/// </summary>
public sealed class CoverageMeasurer
{
    private readonly DirectedGraph _graph;
    private readonly Func<string, string, bool>? _inheritedIcon;

    // Memoises the (is-Real-derived, has-unit) verdict per resolved type class. Concurrent because
    // style checking measures from its worker threads.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (bool, bool)> _unitCache =
        new(StringComparer.Ordinal);

    private int _measured;

    /// <summary>
    /// How many classes this measurer had to work out for itself, rather than finding already done.
    /// The difference between that and the number of classes reported on is what a coverage report
    /// still costs, and it is the number to look at when the first one is slow.
    /// </summary>
    public int MeasuredHere => Volatile.Read(ref _measured);

    public CoverageMeasurer(DirectedGraph graph)
    {
        _graph = graph;
        // For "has an icon" including icons inherited via `extends Modelica.Icons.*`.
        _inheritedIcon = StyleChecking.CreateBaseClassHasIconCallback(graph);
    }

    /// <summary>
    /// This class's contribution, measuring it if nobody has yet. Null for a class with nothing to
    /// measure — a parse failure, a stub, or source that will not parse.
    /// </summary>
    public CoverageFacts? Measure(ModelNode? model)
    {
        if (model is null || model.IsParseFailurePlaceholder || model.IsExternalStub)
            return null;

        if (model.Definition.Coverage is { } already)
            return already;

        // Whoever owned the parse tree keeps it: releasing a tree the caller was still using would
        // cost it the re-parse this class exists to avoid.
        var borrowed = model.Definition.ParsedCode is not null;
        var tree = model.Definition.EnsureParsed();
        if (tree is null)
            return null;

        try
        {
            var facts = Extract(model, tree);
            model.Definition.Coverage = facts;
            Interlocked.Increment(ref _measured);
            return facts;
        }
        catch
        {
            // Skip a model that fails to analyse rather than break the whole dashboard.
            return null;
        }
        finally
        {
            if (!borrowed)
                model.Definition.ParsedCode = null;
        }
    }

    private CoverageFacts Extract(ModelNode model, modelicaParser.Stored_definitionContext tree)
    {
        {
            {
                var iface = ClassInterfaceExtractor.Extract(tree);
                int components = 0, paramTotal = 0, paramWithDesc = 0, realTotal = 0, realWithUnit = 0;

                // Has an icon if it declares its own Icon graphics or inherits one via extends. (Note:
                // ModelNode.IconSvg is populated lazily on render, so it is not usable here.)
                var hasIcon = IconExtractor.ExtractIcon(tree) is not null;
                if (!hasIcon && _inheritedIcon is not null)
                    hasIcon = iface.Elements.Any(e =>
                        e.Kind == ClassElementKind.Extends && _inheritedIcon(e.Type ?? string.Empty, model.Id));

                var imports = iface.Elements
                    .Where(e => e.Kind == ClassElementKind.Import)
                    .Select(e => e.Name)
                    .ToList();

                // Unit coverage counts every Real-derived numeric quantity (plain Real and SI/quantity
                // types that ultimately alias Real). A component is "united" if its type chain fixes a
                // unit (SI types) or it writes an inline unit (plain Real, handled via MissingUnits).
                var plainReals = 0;
                foreach (var element in iface.Elements)
                {
                    if (element.Kind != ClassElementKind.Component)
                        continue;
                    components++;
                    if (element.IsPublic && element.Variability == "parameter")
                    {
                        paramTotal++;
                        if (!string.IsNullOrWhiteSpace(element.Description))
                            paramWithDesc++;
                    }

                    var (isReal, typeHasUnit) = UnitResolver.Resolve(_graph, model.Id, element.Type, imports, _unitCache);
                    if (!isReal)
                        continue;
                    realTotal++;
                    if ((element.Type ?? string.Empty).TrimStart('.').Trim() == "Real")
                        plainReals++;              // unit decided by the inline modifier (below)
                    else if (typeHasUnit)
                        realWithUnit++;            // an SI/quantity type that fixes a unit
                }

                if (plainReals > 0)
                {
                    var unitVisitor = new MissingUnits();
                    unitVisitor.VisitStored_definition(tree);
                    var missing = unitVisitor.Findings.Count(f => f.RuleId == RuleIds.MissingUnit);
                    realWithUnit += Math.Clamp(plainReals - missing, 0, plainReals);
                }

                return new CoverageFacts(
                    HasDescription: !string.IsNullOrWhiteSpace(iface.Description),
                    HasIcon: hasIcon,
                    Components: components,
                    ParameterTotal: paramTotal,
                    ParametersWithDescription: paramWithDesc,
                    RealTotal: realTotal,
                    RealWithUnit: realWithUnit);
            }
        }
    }
}
