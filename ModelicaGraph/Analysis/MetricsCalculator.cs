using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;
using ModelicaParser.Helpers;

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
/// count, and quality-coverage percentages. Coverage is a dedicated pass over each class's structure
/// — never scraped from findings — so it shows the true state whether or not style checking has run
/// and whatever has been waived. Which dimensions are reported is decided from each repository's rule
/// settings (see <see cref="CoverageDimensions.TrackedFor"/>); pass no settings and everything is
/// measured. Pure and side-effect-free (parse trees released).
/// </summary>
public static class MetricsCalculator
{
    /// <param name="settingsFor">The rule settings of the repository a class belongs to, or null for
    /// a class in none. Omit the callback entirely to measure and report every dimension.</param>
    public static LibraryMetrics Compute(
        DirectedGraph graph,
        IEnumerable<ModelNode> models,
        Func<ModelNode, StyleCheckingSettings?>? settingsFor = null)
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
        var dimensions = CoverageDimensions.Ordered;
        var compliant = new int[dimensions.Count];
        var eligible = new int[dimensions.Count];
        var tracked = CoverageDimension.None;
        var components = 0;

        // The settings object is the same for every class in a repository, and working out its
        // dimensions means walking the rule list — so hold the last answer rather than repeating it
        // tens of thousands of times. Only the per-model formatting exclusion varies within a repo.
        StyleCheckingSettings? lastSettings = null;
        var lastMask = CoverageDimension.All;
        var seenSettings = false;

        foreach (var model in classes)
        {
            StyleCheckingSettings? settings = null;
            CoverageDimension mask;
            if (settingsFor is null)
            {
                mask = CoverageDimension.All;
            }
            else
            {
                settings = settingsFor(model);
                if (!seenSettings || !ReferenceEquals(settings, lastSettings))
                {
                    lastSettings = settings;
                    // No repository, no rules: a class nothing checks is on no dimension's report.
                    lastMask = settings is null
                        ? CoverageDimension.None
                        : CoverageDimensions.TrackedFor(settings);
                    seenSettings = true;
                }
                mask = settings is null
                    ? lastMask
                    : CoverageDimensions.ForClass(lastMask, settings, model.Id);
            }

            // Measured once per class and kept. The same class is measured for every scope the
            // dashboard asks about — all libraries, then one sub-package, then each of them side by
            // side — and measuring means parsing it and walking its interface again each time.
            var facts = measurer.Measure(model, mask);
            if (facts is null)
                continue;

            // Narrowed again now the class has been read: whether it opted out of formatting in its
            // own source is a fact only the measurement can supply, and it takes the class off the
            // layout dimensions exactly as the settings' name list does.
            if (settings is not null)
                mask = CoverageDimensions.ForClass(mask, settings, model.Id, facts);

            // Accumulated from the narrowed mask, and only for a class that was measured, because
            // `tracked` decides which rows the report has. Both halves matter: counting a dimension
            // the class was taken off would put back the row the narrowing exists to remove, and
            // counting one for a class that could not be read at all would show "100% (0 of 0)" for
            // a dimension nothing was measured on.
            tracked |= mask;

            components += facts.Components;

            for (int i = 0; i < dimensions.Count; i++)
            {
                var dimension = dimensions[i].Dimension;
                if ((mask & dimension) == 0)
                    continue;

                switch (dimension)
                {
                    case CoverageDimension.ClassDescription:
                        eligible[i]++;
                        if (facts.HasDescription) compliant[i]++;
                        break;
                    case CoverageDimension.DocumentationInfo:
                        eligible[i]++;
                        if (facts.HasDocumentationInfo) compliant[i]++;
                        break;
                    case CoverageDimension.DocumentationRevisions:
                        eligible[i]++;
                        if (facts.HasDocumentationRevisions) compliant[i]++;
                        break;
                    case CoverageDimension.Icon:
                        eligible[i]++;
                        if (facts.HasIcon) compliant[i]++;
                        break;
                    case CoverageDimension.ParameterDescription:
                        eligible[i] += facts.ParameterTotal;
                        compliant[i] += facts.ParametersWithDescription;
                        break;
                    case CoverageDimension.ConstantDescription:
                        eligible[i] += facts.ConstantTotal;
                        compliant[i] += facts.ConstantsWithDescription;
                        break;
                    case CoverageDimension.Unit:
                        eligible[i] += facts.RealTotal;
                        compliant[i] += facts.RealWithUnit;
                        break;
                    default:
                        // A layout dimension: the class as a whole complies or it does not. Every
                        // class counts, including one with nothing to order — it is complying.
                        eligible[i]++;
                        if ((facts.Failed & dimension) == 0) compliant[i]++;
                        break;
                }
            }
        }

        var coverage = new List<CoverageMetric>();
        for (int i = 0; i < dimensions.Count; i++)
        {
            if ((tracked & dimensions[i].Dimension) != 0)
                coverage.Add(new CoverageMetric(dimensions[i].Name, compliant[i], eligible[i]));
        }

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
    private readonly CoverageDimension _dimensions;
    private readonly bool _honorSuppressions;

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

    /// <param name="dimensions">What to measure when <see cref="Measure(ModelNode?)"/> is called
    /// without saying — for style checking, the dimensions its repository tracks, so a class is not
    /// walked again for a rule nobody enabled.</param>
    /// <param name="honorSuppressions">
    /// Whether <c>__MLQT(format=false)</c> takes a class off the layout dimensions, matching the run
    /// this measurement belongs to. False for an audit (<c>mlqt check --no-suppress</c>), which puts
    /// the class's layout findings back — and a report that dropped its layout rows while listing its
    /// layout findings would be the gap-with-no-finding defect the other way round.
    /// </param>
    public CoverageMeasurer(
        DirectedGraph graph,
        CoverageDimension dimensions = CoverageDimension.All,
        bool honorSuppressions = true)
    {
        _graph = graph;
        _dimensions = dimensions;
        _honorSuppressions = honorSuppressions;
        // For "has an icon" including icons inherited via `extends Modelica.Icons.*`.
        _inheritedIcon = StyleChecking.CreateBaseClassHasIconCallback(graph);
    }

    /// <summary>What <see cref="Measure(ModelNode?)"/> measures — the repository-wide answer this
    /// measurer was built for, so a caller can narrow it per class without holding it twice.</summary>
    public CoverageDimension Dimensions => _dimensions;

    /// <summary>
    /// This class's contribution, measuring it if nobody has yet. Null for a class with nothing to
    /// measure — a parse failure, a stub, or source that will not parse.
    /// </summary>
    public CoverageFacts? Measure(ModelNode? model) => Measure(model, _dimensions);

    /// <summary>
    /// As <see cref="Measure(ModelNode?)"/>, for the dimensions this caller needs. A kept measurement
    /// is reused when it already answers for all of them, and widened — not replaced — when it does
    /// not, so a class measured for one repository's rules is not re-walked from scratch for another's.
    /// </summary>
    public CoverageFacts? Measure(ModelNode? model, CoverageDimension needed)
    {
        if (model is null || model.IsParseFailurePlaceholder || model.IsExternalStub)
            return null;

        var already = model.Definition.Coverage;
        if (already is not null && (needed & ~already.Measured) == CoverageDimension.None)
            return already;

        try
        {
            // Borrowed, not taken: style checking calls this with the tree it is still using, and
            // the dashboard calls it for classes nobody else holds. See ModelDefinition.Borrow.
            return model.Definition.Borrow<CoverageFacts?>(
                tree =>
                {
                    var facts = Extract(model, tree, needed | (already?.Measured ?? CoverageDimension.None));
                    model.Definition.Coverage = facts;
                    Interlocked.Increment(ref _measured);
                    return facts;
                },
                null);
        }
        catch
        {
            // Skip a model that fails to analyse rather than break the whole dashboard.
            return null;
        }
    }

    private CoverageFacts Extract(
        ModelNode model, modelicaParser.Stored_definitionContext tree, CoverageDimension needed)
    {
        var iface = ClassInterfaceExtractor.Extract(tree);
        int components = 0, paramTotal = 0, paramWithDesc = 0, realTotal = 0, realWithUnit = 0;
        int constantTotal = 0, constantWithDesc = 0;

        // Has an icon if it declares its own Icon graphics or inherits one via extends. (Note:
        // ModelNode.IconSvg is populated lazily on render, so it is not usable here.)
        var hasIcon = false;
        if ((needed & CoverageDimension.Icon) != 0)
        {
            hasIcon = IconExtractor.ExtractIcon(tree) is not null;
            if (!hasIcon && _inheritedIcon is not null)
                hasIcon = iface.Elements.Any(e =>
                    e.Kind == ClassElementKind.Extends && _inheritedIcon(e.Type ?? string.Empty, model.Id));
        }

        var imports = iface.Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)
            .ToList();

        // Unit coverage counts every Real-derived numeric quantity (plain Real and SI/quantity types
        // that ultimately alias Real). Which of them are united is decided by the MissingUnits rule
        // below, run with the same type lookup — so a gap on the dashboard is a finding in the report
        // and the other way round. Counting it here on its own terms is how the two came to disagree.
        var wantsUnits = (needed & CoverageDimension.Unit) != 0;
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
            else if (element.IsPublic && element.Variability == "constant")
            {
                constantTotal++;
                if (!string.IsNullOrWhiteSpace(element.Description))
                    constantWithDesc++;
            }

            if (!wantsUnits)
                continue;

            var (isReal, _) = UnitResolver.Resolve(_graph, model.Id, element.Type, imports, _unitCache);
            if (isReal)
                realTotal++;
        }

        if (realTotal > 0)
        {
            // The rule's verdict, not a second opinion: united means "the rule does not flag it".
            //
            // Counted for this class only. The visitor walks into a nested class carrying
            // `replaceable`, and those components are measured with the nested class's own node, so
            // counting them here would subtract them from a denominator that never included them.
            var unitVisitor = new MissingUnits(
                ModelicaName.EnclosingPackageOf(model.Id),
                unitLookup: (_, typeName) =>
                    UnitResolver.Resolve(_graph, model.Id, typeName, imports, _unitCache));
            unitVisitor.VisitStored_definition(tree);
            var missing = unitVisitor.Findings.Count(
                f => f.RuleId == RuleIds.MissingUnit && f.ModelId == model.Id);
            realWithUnit = Math.Clamp(realTotal - missing, 0, realTotal);
        }

        var (hasDocInfo, hasDocRevisions) = MeasureDocumentation(tree, model.Id, needed);

        return new CoverageFacts(
            HasDescription: !string.IsNullOrWhiteSpace(iface.Description),
            HasIcon: hasIcon,
            Components: components,
            ParameterTotal: paramTotal,
            ParametersWithDescription: paramWithDesc,
            RealTotal: realTotal,
            RealWithUnit: realWithUnit,
            HasDocumentationInfo: hasDocInfo,
            HasDocumentationRevisions: hasDocRevisions,
            ConstantTotal: constantTotal,
            ConstantsWithDescription: constantWithDesc,
            Measured: needed,
            Failed: MeasureLayout(tree, model.Id, needed),
            FormattingPreserved: PreservesFormatting(model));
    }

    /// <summary>
    /// Whether the class opted out of formatting in its own source, with <c>__MLQT(format=false)</c>
    /// or <c>__MLQT(preserveOrder=true)</c>, <b>as this run reads it</b>.
    ///
    /// <para>Recorded on the facts because a report decides which dimensions a class is on from its
    /// repository's settings, and a settings object cannot know something the source says — so
    /// without this, the in-source exclusion silenced the layout rules while the dashboard went on
    /// counting the class, which is a gap no finding would ever name.</para>
    ///
    /// <para>False in an audit run, where the directives are deliberately not being read and the
    /// class's layout findings are therefore back. That makes the fact one about a run rather than
    /// only about the source, so a facts cache belongs to a run with one suppression mode: the CLI is
    /// a process per run, and the app and the MCP server always honour them.</para>
    ///
    /// <para>Read through <see cref="ClassSuppressions"/>, which keeps the answer on the class — the
    /// style checker has usually just asked the same question of the same tree.</para>
    /// </summary>
    private bool PreservesFormatting(ModelNode model) =>
        _honorSuppressions && ClassSuppressions.For(model.Definition, model.Id).PreservesFormatting(model.Id);

    /// <summary>
    /// Documentation info/revisions, from the rule's own annotation walk — one traversal answering
    /// for both, and only when either is wanted.
    /// </summary>
    private static (bool Info, bool Revisions) MeasureDocumentation(
        modelicaParser.Stored_definitionContext tree, string modelId, CoverageDimension needed)
    {
        var wantsInfo = (needed & CoverageDimension.DocumentationInfo) != 0;
        var wantsRevisions = (needed & CoverageDimension.DocumentationRevisions) != 0;
        if (!wantsInfo && !wantsRevisions)
            return (false, false);

        var missing = FailedRules(
            new CheckClassAnnotations(
                wantsInfo, wantsRevisions, checkIcon: false, ModelicaName.EnclosingPackageOf(modelId)),
            tree, modelId);

        return (wantsInfo && !missing.Contains(RuleIds.ClassDocumentationInfo),
                wantsRevisions && !missing.Contains(RuleIds.ClassDocumentationRevisions));
    }

    /// <summary>
    /// The rule ids <paramref name="visitor"/> reports <b>about <paramref name="modelId"/> itself</b>.
    ///
    /// <para>Both halves matter and both were missing. A rule visitor walks into a nested
    /// <c>replaceable</c>/<c>redeclare</c> class — deliberately, because such a class cannot be
    /// parsed on its own — and attributes what it finds there to that nested class. Taking every
    /// finding the visitor produced therefore recorded the <em>parent</em> as failing a dimension its
    /// child failed: a tidy class holding a <c>replaceable</c> model whose import was out of place
    /// read as failing <i>Imports first</i>, while the only finding the checker raises names the
    /// nested class. The dashboard then showed a gap no finding would ever name — the one thing
    /// <see cref="CoverageDimensions"/> exists to prevent — and, because the nested class has a node
    /// of its own and is measured in its own right, one problem cost two classes in the denominator.
    /// This is the walk <c>StyleCheckRunner.OnlyAbout</c> exists for on the finding side, and the one
    /// the unit measurement above already guards against by hand.</para>
    ///
    /// <para>And the visitor has to be given the class's enclosing package, or the ids it produces are
    /// bare names and match nothing: <c>Lib.Sub.Outer</c> is reported as <c>Outer</c>, so a filter
    /// without the base package would drop every finding rather than only the nested ones.</para>
    /// </summary>
    private static HashSet<string> FailedRules(
        VisitorWithModelNameTracking visitor,
        modelicaParser.Stored_definitionContext tree,
        string modelId)
    {
        visitor.VisitStored_definition(tree);
        return visitor.Findings
            .Where(f => f.ModelId == modelId)
            .Select(f => f.RuleId)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The layout dimensions this class fails, by running the rules' own visitors rather than reading
    /// their findings: the same verdict, but one that a waiver or a baseline cannot hide, which is
    /// what makes it coverage rather than a second finding count. Each visitor is asked for only what
    /// was requested, and one that nobody asked about is never run.
    /// </summary>
    private static CoverageDimension MeasureLayout(
        modelicaParser.Stored_definitionContext tree, string modelId, CoverageDimension needed)
    {
        var wanted = needed & CoverageDimension.Layout;
        if (wanted == CoverageDimension.None)
            return CoverageDimension.None;

        var failed = CoverageDimension.None;
        var broken = new HashSet<string>(StringComparer.Ordinal);
        var basePackage = ModelicaName.EnclosingPackageOf(modelId);

        // Only what this class itself failed — see FailedRules for why that is not the same as what
        // the visitor reported.
        void Run(VisitorWithModelNameTracking visitor)
            => broken.UnionWith(FailedRules(visitor, tree, modelId));

        if ((wanted & CoverageDimension.ImportsFirst) != 0)
            Run(new ImportStatementsFirst(true, basePackage));
        if ((wanted & CoverageDimension.ExtendsAtTop) != 0)
            Run(new ExtendsClausesAtTop(false, basePackage));
        if ((wanted & (CoverageDimension.InitialSectionsFirst | CoverageDimension.InitialSectionsLast)) != 0)
            Run(new InitialEquationFirst(
                (wanted & CoverageDimension.InitialSectionsFirst) != 0,
                (wanted & CoverageDimension.InitialSectionsLast) != 0,
                basePackage));
        if ((wanted & (CoverageDimension.OneOfEachSection | CoverageDimension.EquationAlgorithmNotMixed)) != 0)
        {
            var sections = (wanted & CoverageDimension.OneOfEachSection) != 0;
            Run(new OneOfEachSection(
                sections, sections, sections, sections,
                allowEquationAndAlgorithm: (wanted & CoverageDimension.EquationAlgorithmNotMixed) == 0,
                basePackage: basePackage));
        }
        if ((wanted & CoverageDimension.ConnectionsNotMixed) != 0)
            Run(new MixConnectionsAndEquations(basePackage));

        if (broken.Contains(RuleIds.ImportStatementsFirst)) failed |= CoverageDimension.ImportsFirst;
        if (broken.Contains(RuleIds.ExtendsAtTop)) failed |= CoverageDimension.ExtendsAtTop;
        if (broken.Contains(RuleIds.OneOfEachSection)) failed |= CoverageDimension.OneOfEachSection;
        if (broken.Contains(RuleIds.InitialEqAlgoFirst)) failed |= CoverageDimension.InitialSectionsFirst;
        if (broken.Contains(RuleIds.InitialEqAlgoLast)) failed |= CoverageDimension.InitialSectionsLast;
        if (broken.Contains(RuleIds.DontMixEquationAndAlgorithm)) failed |= CoverageDimension.EquationAlgorithmNotMixed;
        if (broken.Contains(RuleIds.DontMixConnections)) failed |= CoverageDimension.ConnectionsNotMixed;

        return failed & wanted;
    }
}
