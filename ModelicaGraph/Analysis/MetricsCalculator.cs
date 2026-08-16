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
public sealed record LibraryMetrics(
    int TotalClasses,
    IReadOnlyDictionary<string, int> ClassesByType,
    int TotalComponents,
    IReadOnlyList<CoverageMetric> Coverage);

/// <summary>
/// Computes the metrics-dashboard figures over a set of models: class counts by kind, total component
/// count, and quality-coverage percentages (description, icon, parameter description, unit). Coverage is
/// a dedicated pass over each class's structure — independent of which rules are enabled and of whether
/// style checking has run — so the dashboard is always available and shows the true state (waivers are
/// ignored: a suppressed gap still counts as a gap). Pure and side-effect-free (parse trees released).
/// </summary>
public static class MetricsCalculator
{
    public static LibraryMetrics Compute(IEnumerable<ModelNode> models)
    {
        var classes = models.Where(m => m is not null && !m.IsParseFailurePlaceholder).ToList();
        var total = classes.Count;
        var byType = classes
            .GroupBy(m => string.IsNullOrEmpty(m.ClassType) ? "unknown" : m.ClassType, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        int withDescription = 0, withIcon = 0, components = 0;
        int paramTotal = 0, paramWithDesc = 0;
        int realTotal = 0, realWithUnit = 0;

        foreach (var model in classes)
        {
            var tree = model.Definition.EnsureParsed();
            if (tree is null)
                continue;

            try
            {
                // A source-based icon check: does the class declare Icon graphics of its own? (Note:
                // ModelNode.IconSvg is populated lazily on render, so it is not usable here. Icons
                // inherited via `extends Modelica.Icons.*` are not counted — a later refinement.)
                if (IconExtractor.ExtractIcon(tree) is not null)
                    withIcon++;

                var iface = ClassInterfaceExtractor.Extract(tree);
                if (!string.IsNullOrWhiteSpace(iface.Description))
                    withDescription++;

                var modelReals = 0;
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
                    if (element.Type == "Real")
                        modelReals++;
                }

                if (modelReals > 0)
                {
                    var unitVisitor = new MissingUnits();
                    unitVisitor.VisitStored_definition(tree);
                    var missing = unitVisitor.Findings.Count(f => f.RuleId == RuleIds.MissingUnit);
                    realTotal += modelReals;
                    realWithUnit += Math.Clamp(modelReals - missing, 0, modelReals);
                }
            }
            catch
            {
                // Skip a model that fails to analyse rather than break the whole dashboard.
            }
            finally
            {
                model.Definition.ParsedCode = null;
            }
        }

        var coverage = new List<CoverageMetric>
        {
            new("Description", withDescription, total),
            new("Own icon", withIcon, total),
            new("Parameter description", paramWithDesc, paramTotal),
            new("Real vars w/ unit", realWithUnit, realTotal),
        };

        return new LibraryMetrics(total, byType, components, coverage);
    }
}
