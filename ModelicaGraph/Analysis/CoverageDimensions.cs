using ModelicaGraph.DataTypes;
using ModelicaParser.StyleRules;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Names the coverage dimensions and decides which of them a repository is tracking.
///
/// <para>Two things take a dimension off the report. A rule set to Off is the user saying the gap
/// does not matter to them, and a number nobody will act on is worse than no number — it drags the
/// average and makes the trend read as debt. And a gap the formatter closes on every save is not
/// debt either: reporting "imports first: 96%" to someone whose formatter puts imports first on
/// write measures the moment before the save, not the library.</para>
/// </summary>
public static class CoverageDimensions
{
    /// <summary>Every dimension in the order a report lists them, with the name used as its label and
    /// as its key in the persisted history — so these strings are load-bearing once shipped.</summary>
    public static readonly IReadOnlyList<(CoverageDimension Dimension, string Name)> Ordered =
    [
        (CoverageDimension.ClassDescription, "Class description"),
        (CoverageDimension.DocumentationInfo, "Documentation info"),
        (CoverageDimension.DocumentationRevisions, "Documentation revisions"),
        (CoverageDimension.Icon, "Icon"),
        (CoverageDimension.ParameterDescription, "Parameter description"),
        (CoverageDimension.ConstantDescription, "Constant description"),
        (CoverageDimension.Unit, "Unit"),
        (CoverageDimension.ImportsFirst, "Imports first"),
        (CoverageDimension.ExtendsAtTop, "Extends at top"),
        (CoverageDimension.OneOfEachSection, "One of each section"),
        (CoverageDimension.InitialSectionsFirst, "Initial sections first"),
        (CoverageDimension.InitialSectionsLast, "Initial sections last"),
        (CoverageDimension.EquationAlgorithmNotMixed, "Equation/algorithm not mixed"),
        (CoverageDimension.ConnectionsNotMixed, "Connections not mixed"),
    ];

    /// <summary>The rule whose severity decides whether a dimension is tracked. Each dimension names
    /// its own rule, including the ones with no setting of their own: <c>ExtendsAtTop</c> is governed
    /// by <c>ImportStatementsFirst</c>, and <c>StyleCheckingSettings.SeverityFor</c> resolves that
    /// (see <see cref="RuleDefinition.GovernedBy"/>). Naming the governor here instead would state the
    /// same coupling a second time, in a file that would not be edited when it changed.</summary>
    private static string RuleFor(CoverageDimension dimension) => dimension switch
    {
        CoverageDimension.ClassDescription => RuleIds.ClassDescription,
        CoverageDimension.DocumentationInfo => RuleIds.ClassDocumentationInfo,
        CoverageDimension.DocumentationRevisions => RuleIds.ClassDocumentationRevisions,
        CoverageDimension.Icon => RuleIds.ClassIcon,
        CoverageDimension.ParameterDescription => RuleIds.ParameterDescription,
        CoverageDimension.ConstantDescription => RuleIds.ConstantDescription,
        CoverageDimension.Unit => RuleIds.MissingUnit,
        CoverageDimension.ImportsFirst => RuleIds.ImportStatementsFirst,
        CoverageDimension.ExtendsAtTop => RuleIds.ExtendsAtTop,
        CoverageDimension.OneOfEachSection => RuleIds.OneOfEachSection,
        CoverageDimension.InitialSectionsFirst => RuleIds.InitialEqAlgoFirst,
        CoverageDimension.InitialSectionsLast => RuleIds.InitialEqAlgoLast,
        CoverageDimension.EquationAlgorithmNotMixed => RuleIds.DontMixEquationAndAlgorithm,
        CoverageDimension.ConnectionsNotMixed => RuleIds.DontMixConnections,
        _ => string.Empty
    };

    /// <summary>
    /// What the formatter rewrites, and therefore what is not worth reporting when it runs. The
    /// renderer only reorders inside its one-of-each-section branch, and only moves imports when told
    /// to, so with that rule off the formatter leaves layout alone and the dimensions stay on the
    /// report.
    ///
    /// <para><c>InitialSectionsLast</c> is in here now. It used not to be, because the renderer wrote
    /// initial sections first whatever the setting said — so the formatter <em>defeated</em> that rule
    /// rather than satisfying it, and the report had to keep showing a gap that would be reintroduced
    /// on the next save. The renderer takes the convention now
    /// (<c>FormattingOptions.InitialSectionsLast</c>), so the dimension belongs with the others.</para>
    /// </summary>
    private const CoverageDimension FormatterRewrites =
        CoverageDimension.ImportsFirst | CoverageDimension.ExtendsAtTop
        | CoverageDimension.OneOfEachSection | CoverageDimension.InitialSectionsFirst
        | CoverageDimension.InitialSectionsLast;

    /// <summary>
    /// The dimensions to measure and report for a class in a repository configured this way.
    /// A class belonging to no repository has no settings and is tracked for nothing — style
    /// checking runs per repository, so no rule ever reports on it either.
    /// </summary>
    /// <param name="modelId">The class, for the exclusions that are decided per class — see
    /// <see cref="ForClass"/>. Omit it for the repository-wide answer.</param>
    public static CoverageDimension TrackedFor(StyleCheckingSettings settings, string? modelId = null)
    {
        var tracked = CoverageDimension.None;
        foreach (var (dimension, _) in Ordered)
        {
            if (settings.IsRuleEnabled(RuleFor(dimension)))
                tracked |= dimension;
        }

        if (settings.ApplyFormattingRules && settings.OneOfEachSection)
            tracked &= ~FormatterRewrites;

        return modelId is { Length: > 0 } ? ForClass(tracked, settings, modelId) : tracked;
    }

    /// <summary>
    /// Narrows a repository's tracked dimensions to what applies to one class.
    ///
    /// <para><b>Every</b> way of taking a class out of scope is asked here, and that is the point of
    /// gathering it in one method: each of the three arrived separately, and each was taught to the
    /// checker before anything asked what it meant for the report.</para>
    ///
    /// <para><b>An excluded library is on nothing.</b>
    /// <see cref="StyleCheckingSettings.ExcludedLibraries"/> — typically the examples or the test
    /// library sharing the repository — makes
    /// <see cref="StyleChecking.RunStyleCheckingFindings"/> return before it reads the class and
    /// <c>GraphAnalysisRunner</c> drop it from the analysed set, so no rule will ever report anything
    /// about it. Counting it on every dimension therefore put a whole library's worth of gaps into
    /// percentages, into the recorded trend and into the <c>--min-coverage</c> gate that no finding
    /// would ever name — the same defect the formatting exclusions had, on the mechanism nobody had
    /// asked about.</para>
    ///
    /// <para><b>A class excluded from formatting is off the layout dimensions.</b> The style checker
    /// skips its layout rules (<c>isExcludedFromFormatting</c> in
    /// <see cref="StyleChecking.RunStyleCheckingFindings"/>), so counting them here would report a gap
    /// no rule will ever raise. Both ways of saying it count. The name list in
    /// <see cref="StyleCheckingSettings.FormattingExcludedModels"/> is visible from the settings
    /// alone; <c>__MLQT(format=false)</c> / <c>preserveOrder=true</c> is a fact about the source, and
    /// reaches here through <see cref="CoverageFacts.FormattingPreserved"/>, recorded while the class
    /// was measured. Phase 5b calls the annotation the rename-safe successor to the list and the
    /// documentation steers new usage to it, so the successor behaving worse — silenced in the
    /// checker, still counted on the dashboard — was the wrong way round.</para>
    ///
    /// <para>The callers that used to spell this out separately (the metrics report, the GUI's
    /// coverage sweep, the check-time measurer) are why it is a method: a mechanism had been added to
    /// one of them and to none of the others.</para>
    /// </summary>
    /// <param name="tracked">The repository-wide answer from <see cref="TrackedFor"/>.</param>
    /// <param name="facts">The class's measurement, when it has one. Null before it is measured, in
    /// which case only the name list can be consulted — enough for deciding what to measure, and
    /// narrowed again on the way into a report.</param>
    public static CoverageDimension ForClass(
        CoverageDimension tracked, StyleCheckingSettings settings, string modelId,
        CoverageFacts? facts = null)
    {
        if (settings.IsLibraryExcluded(modelId))
            return CoverageDimension.None;

        if ((tracked & CoverageDimension.Layout) == 0)
            return tracked;

        var excluded =
            (settings.FormattingExcludedModels.Count > 0 && settings.IsModelExcludedFromFormatting(modelId))
            || facts is { FormattingPreserved: true };

        return excluded ? tracked & ~CoverageDimension.Layout : tracked;
    }
}
