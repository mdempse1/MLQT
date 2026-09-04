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
    /// <param name="modelId">The class, for the per-model formatting exclusion. A class excluded from
    /// formatting has its layout rules skipped by the style checker, so counting it here would report
    /// a gap no rule will ever raise.</param>
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

        if (modelId is { Length: > 0 }
            && settings.FormattingExcludedModels.Count > 0
            && settings.IsModelExcludedFromFormatting(modelId))
            tracked &= ~CoverageDimension.Layout;

        return tracked;
    }
}
