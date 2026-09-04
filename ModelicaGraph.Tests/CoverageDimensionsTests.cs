using System.Collections.Generic;
using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Which coverage dimensions a repository tracks, and how that decides the rows a report has. The
/// point of the gate: a rule the user switched off is a gap they have decided not to care about, and
/// a gap the formatter closes on every save was never debt.
/// </summary>
public class CoverageDimensionsTests
{
    private static ModelNode Model(string id, string code, string classType = "model")
        => new(id, id, code) { ClassType = classType };

    private static DirectedGraph BuildGraph(IEnumerable<ModelNode> models)
    {
        var graph = new DirectedGraph();
        foreach (var m in models)
            graph.AddNode(m);
        return graph;
    }

    private static IEnumerable<string> Rows(LibraryMetrics metrics)
        => metrics.Coverage.Select(c => c.Dimension);

    [Fact]
    public void NoRulesEnabled_TracksNothing()
    {
        Assert.Equal(CoverageDimension.None, CoverageDimensions.TrackedFor(new StyleCheckingSettings()));
    }

    [Fact]
    public void OnlyEnabledRulesAreTracked()
    {
        var settings = new StyleCheckingSettings { ClassHasDescription = true, ClassHasIcon = true };

        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.Equal(CoverageDimension.ClassDescription | CoverageDimension.Icon, tracked);
    }

    [Fact]
    public void ExtendsAtTop_FollowsTheImportsRule_HavingNoToggleOfItsOwn()
    {
        // The style checker runs ExtendsClausesAtTop alongside ImportStatementsFirst rather than from
        // a severity of its own, so gating it on its own rule id would keep it permanently off.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.True(tracked.HasFlag(CoverageDimension.ImportsFirst));
        Assert.True(tracked.HasFlag(CoverageDimension.ExtendsAtTop));
    }

    [Fact]
    public void FormatterRewritesLayout_SoThoseDimensionsAreNotTracked()
    {
        var settings = new StyleCheckingSettings
        {
            ApplyFormattingRules = true,
            OneOfEachSection = true,
            ImportStatementsFirst = true,
            InitialEQAlgoFirst = true,
            DontMixConnections = true,
            ClassHasDescription = true,
        };

        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.False(tracked.HasFlag(CoverageDimension.ImportsFirst));
        Assert.False(tracked.HasFlag(CoverageDimension.ExtendsAtTop));
        Assert.False(tracked.HasFlag(CoverageDimension.OneOfEachSection));
        Assert.False(tracked.HasFlag(CoverageDimension.InitialSectionsFirst));
        // The renderer cannot fix these, so they stay on the report even with the formatter running.
        Assert.True(tracked.HasFlag(CoverageDimension.ConnectionsNotMixed));
        Assert.True(tracked.HasFlag(CoverageDimension.ClassDescription));
    }

    /// <summary>
    /// This used to assert the opposite, and the comment explained why: the renderer wrote initial
    /// sections first whatever the setting said, so the formatter defeated this rule rather than
    /// satisfying it, and hiding the number as "the formatter handles it" would have been a lie. The
    /// renderer takes the convention now, so the dimension drops off with the other layout ones.
    /// </summary>
    [Fact]
    public void InitialSectionsLast_DropsOff_NowTheFormatterHonoursIt()
    {
        var settings = new StyleCheckingSettings
        {
            ApplyFormattingRules = true,
            OneOfEachSection = true,
            InitialEQAlgoLast = true,
        };

        Assert.False(CoverageDimensions.TrackedFor(settings).HasFlag(CoverageDimension.InitialSectionsLast));
    }

    [Fact]
    public void InitialSectionsLast_StaysTracked_WhenTheFormatterIsNotRunning()
    {
        // Nothing is rewriting the class, so the gap is real debt and worth measuring.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, InitialEQAlgoLast = true };

        Assert.True(CoverageDimensions.TrackedFor(settings).HasFlag(CoverageDimension.InitialSectionsLast));
    }

    [Fact]
    public void FormatterOff_KeepsLayoutDimensions()
    {
        // The case this feature exists for: no formatter, but the layout rules still enforced.
        var settings = new StyleCheckingSettings
        {
            ApplyFormattingRules = false,
            OneOfEachSection = true,
            ImportStatementsFirst = true,
        };

        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.True(tracked.HasFlag(CoverageDimension.OneOfEachSection));
        Assert.True(tracked.HasFlag(CoverageDimension.ImportsFirst));
    }

    [Fact]
    public void FormattingIsOn_ButThisClassIsExcludedFromIt_SoLayoutIsStillNotTracked()
    {
        // The style checker skips the layout rules for a class excluded from formatting, so counting
        // it would report a gap no rule will ever raise.
        var settings = new StyleCheckingSettings
        {
            OneOfEachSection = true,
            DontMixConnections = true,
            ClassHasDescription = true,
        };
        settings.FormattingExcludedModels.Add("A");

        var tracked = CoverageDimensions.TrackedFor(settings, "A");

        Assert.Equal(CoverageDimension.None, tracked & CoverageDimension.Layout);
        Assert.True(tracked.HasFlag(CoverageDimension.ClassDescription));
        Assert.True(CoverageDimensions.TrackedFor(settings, "B").HasFlag(CoverageDimension.OneOfEachSection));
    }

    [Fact]
    public void RowsAreOnlyTheTrackedDimensions()
    {
        var models = new[] { Model("A", "model A \"d\"\n  parameter Real k = 1 \"g\";\nend A;") };
        var settings = new StyleCheckingSettings { ClassHasDescription = true };

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models, _ => settings);

        Assert.Equal(new[] { "Class description" }, Rows(metrics));
    }

    [Fact]
    public void ClassInNoRepository_IsOnNoRow()
    {
        // Style checking runs per repository, so a class outside every repository is checked by no
        // rule — reporting coverage for it would put a number on code nothing else measures.
        var models = new[] { Model("A", "model A \"d\" end A;") };

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models, _ => null);

        Assert.Empty(metrics.Coverage);
        Assert.Equal(1, metrics.TotalClasses);   // still counted as a class; only coverage is gated
    }

    [Fact]
    public void TwoRepositories_ListADimensionEitherOneTracks_CountingOnlyItsOwnClasses()
    {
        var models = new[]
        {
            Model("A", "model A \"described\" end A;"),
            Model("B", "model B end B;"),
        };
        var tracking = new StyleCheckingSettings { ClassHasDescription = true };
        var indifferent = new StyleCheckingSettings { ClassHasIcon = true };

        var metrics = MetricsCalculator.Compute(
            BuildGraph(models), models, m => m.Id == "A" ? tracking : indifferent);

        var description = metrics.Coverage.Single(c => c.Dimension == "Class description");
        Assert.Equal(1, description.Eligible);   // only A's repository asked for it
        Assert.Equal(1, description.Compliant);
        Assert.Contains("Icon", Rows(metrics));
    }

    [Fact]
    public void NoSettingsCallback_ReportsEveryDimension()
    {
        // The standalone shape — a test, a snippet, a single library with no repository behind it.
        var models = new[] { Model("A", "model A \"d\" end A;") };

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models);

        Assert.Equal(CoverageDimensions.Ordered.Select(d => d.Name), Rows(metrics));
    }

    // ---- the two formatting exclusions, which must behave the same (B39/B40) ------------------

    private const string ImportsLate = """
        model A
          Real x "state";
          import Modelica.SIunits;
        equation
          x = time;
        end A;
        """;

    private const string PreserveOrderAnnotation =
        """
        annotation(__MLQT(preserveOrder=true, reason="solver order matters"));
        """;

    private static string PreserveOrder(string body) =>
        body.Replace("end A;", PreserveOrderAnnotation + "end A;");

    [Fact]
    public void AClassThatOptedOutOfFormattingInSource_IsOffTheLayoutDimensions()
    {
        // __MLQT(preserveOrder=true) is the rename-safe successor to FormattingExcludedModels, and
        // the checker already skips the layout rules for it. Counting the class here would report a
        // gap no finding will ever name - the one thing coverage must not do.
        var models = new[] { Model("A", PreserveOrder(ImportsLate)) };
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models, _ => settings);

        Assert.DoesNotContain("Imports first", Rows(metrics));
    }

    [Fact]
    public void TheSameClassWithoutTheAnnotation_IsOnThem()
    {
        var models = new[] { Model("A", ImportsLate) };
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models, _ => settings);

        var importsFirst = metrics.Coverage.Single(c => c.Dimension == "Imports first");
        Assert.Equal(1, importsFirst.Eligible);
        Assert.Equal(0, importsFirst.Compliant);   // the import is not first
    }

    [Fact]
    public void TheTwoExclusionsAgree()
    {
        // The point of the pair: the in-source one used to be silenced in the checker and still
        // counted on the dashboard, while the name list did both. Whatever the answer is, it is the
        // same answer.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };

        var byAnnotation = MetricsCalculator.Compute(
            BuildGraph([Model("A", PreserveOrder(ImportsLate))]),
            [Model("A", PreserveOrder(ImportsLate))], _ => settings);

        var named = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };
        named.FormattingExcludedModels.Add("A");
        var byName = MetricsCalculator.Compute(
            BuildGraph([Model("A", ImportsLate)]), [Model("A", ImportsLate)], _ => named);

        Assert.Equal(Rows(byName), Rows(byAnnotation));
    }

    [Fact]
    public void ForClass_IsWhatEveryCallerAsks()
    {
        // The narrowing lives in one method because it had been written out in three places, and the
        // annotation reached only one of them. TrackedFor(settings, id) is that method's front door.
        var settings = new StyleCheckingSettings { OneOfEachSection = true, ClassHasDescription = true };
        settings.FormattingExcludedModels.Add("A");
        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.Equal(
            CoverageDimensions.TrackedFor(settings, "A"),
            CoverageDimensions.ForClass(tracked, settings, "A"));

        var preserved = new CoverageFacts(false, false, 0, 0, 0, 0, 0, FormattingPreserved: true);
        Assert.Equal(
            CoverageDimension.None,
            CoverageDimensions.ForClass(tracked, settings, "B", preserved) & CoverageDimension.Layout);
        Assert.True(CoverageDimensions.ForClass(tracked, settings, "B").HasFlag(CoverageDimension.OneOfEachSection));
    }

    // ---- an audit run reads no directives, so it keeps the rows they would remove (B52) ---------

    [Fact]
    public void AnAuditRun_KeepsTheLayoutRowsTheAnnotationWouldDrop()
    {
        // --no-suppress puts the class's layout findings back. A report that dropped its layout rows
        // while listing its layout findings is the gap-with-no-finding defect the other way round.
        var model = Model("A", PreserveOrder(ImportsLate));
        var graph = BuildGraph([model]);

        var audited = new CoverageMeasurer(graph, CoverageDimension.All, honorSuppressions: false)
            .Measure(model);

        Assert.NotNull(audited);
        Assert.False(audited!.FormattingPreserved);

        var settings = new StyleCheckingSettings { OneOfEachSection = true, ImportStatementsFirst = true };
        var tracked = CoverageDimensions.TrackedFor(settings);
        Assert.True(CoverageDimensions.ForClass(tracked, settings, "A", audited)
            .HasFlag(CoverageDimension.ImportsFirst));
    }

    [Fact]
    public void AnOrdinaryRun_StillDropsThem()
    {
        var model = Model("A", PreserveOrder(ImportsLate));

        var facts = new CoverageMeasurer(BuildGraph([model]), CoverageDimension.All).Measure(model);

        Assert.NotNull(facts);
        Assert.True(facts!.FormattingPreserved);
    }

    // ---- the third exclusion: a whole library the settings leave alone (B50) -------------------

    private static StyleCheckingSettings Excluding(string library)
    {
        var settings = new StyleCheckingSettings
        {
            ClassHasDescription = true,
            ClassHasIcon = true,
            OneOfEachSection = true,
            ImportStatementsFirst = true,
        };
        settings.ExcludedLibraries.Add(library);
        return settings;
    }

    [Fact]
    public void AClassInAnExcludedLibrary_IsOnNoDimensionAtAll()
    {
        // Not just the layout ones: RunStyleCheckingFindings returns before it reads the class, so
        // no rule of any kind will report on it. Counting it put a whole library's undocumented
        // classes into the percentages, the trend and the --min-coverage gate with nothing that would
        // ever name them.
        var settings = Excluding("Tests");

        Assert.Equal(
            CoverageDimension.None,
            CoverageDimensions.ForClass(CoverageDimensions.TrackedFor(settings), settings, "Tests.Case"));
    }

    [Fact]
    public void AClassOutsideTheExcludedLibrary_IsUnaffected()
    {
        var settings = Excluding("Tests");
        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.Equal(tracked, CoverageDimensions.ForClass(tracked, settings, "Lib.Thing"));
        Assert.NotEqual(CoverageDimension.None, tracked);
    }

    [Fact]
    public void AnExcludedLibrary_IsInNoCoverageDenominator()
    {
        // The whole defect, end to end: the excluded library is missing every description, and the
        // library under check has them all. Coverage must read 100%, not 50%.
        var models = new[]
        {
            Model("Lib.Good", "model Good \"documented\" end Good;"),
            Model("Tests.Bad", "model Bad end Bad;"),
        };
        var settings = Excluding("Tests");

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models, _ => settings);

        var descriptions = metrics.Coverage.Single(c => c.Dimension == "Class description");
        Assert.Equal(1, descriptions.Eligible);
        Assert.Equal(1, descriptions.Compliant);
    }

    [Fact]
    public void AnExcludedLibraryIsStillInTheClassCensus()
    {
        // Excluding a library suppresses the reporting, not the library. It is still the team's code
        // and still on the Size panel; what it must not do is drag a quality percentage nobody can
        // act on.
        var models = new[]
        {
            Model("Lib.Good", "model Good \"documented\" end Good;"),
            Model("Tests.Bad", "model Bad end Bad;"),
        };

        var metrics = MetricsCalculator.Compute(BuildGraph(models), models, _ => Excluding("Tests"));

        Assert.Equal(2, metrics.TotalClasses);
    }

    // ---- every dimension is wired to a rule that exists (B59) ----------------------------------

    [Fact]
    public void EveryDimensionIsListedAndBoundToARuleTheCatalogHas()
    {
        // TrackedFor walks Ordered and enables each dimension from the rule RuleFor names. A
        // dimension missing from Ordered never reaches a report; one whose rule id falls through to
        // the empty string is never enabled. Both are silent, which is what this is for.
        var declared = System.Enum.GetValues<CoverageDimension>()
            .Where(d => d is not (CoverageDimension.None or CoverageDimension.Layout or CoverageDimension.All))
            .ToList();
        var listed = CoverageDimensions.Ordered.Select(o => o.Dimension).ToList();

        Assert.Equal(declared.OrderBy(d => (int)d), listed.OrderBy(d => (int)d));

        // Every rule id a dimension resolves through is one the catalog knows. Asked by enabling
        // every rule at once: a dimension bound to "" or to an id nobody registered stays off.
        var all = new StyleCheckingSettings();
        foreach (var rule in ModelicaParser.StyleRules.RuleCatalog.Configurable)
            all.SetRuleEnabled(rule.Id, true);
        all.ApplyFormattingRules = false;   // or the formatter-maintained dimensions drop off

        var tracked = CoverageDimensions.TrackedFor(all);
        var missing = listed.Where(d => (tracked & d) == 0).ToList();

        Assert.True(missing.Count == 0,
            "every rule is enabled, so every dimension should be tracked. Not tracked: " +
            string.Join(", ", missing));
    }

    [Fact]
    public void TheLayoutDimensionsAreExactlyTheRulesTheCheckerSkipsForAnExcludedClass()
    {
        // CoverageDimension.Layout is what ForClass drops for a class excluded from formatting, and
        // it is right only while it names the same rules StyleChecking puts behind
        // isExcludedFromFormatting. The two are in different files and nothing else ties them.
        var skipped = new[]
        {
            CoverageDimension.ImportsFirst,
            CoverageDimension.ExtendsAtTop,
            CoverageDimension.InitialSectionsFirst,
            CoverageDimension.InitialSectionsLast,
            CoverageDimension.OneOfEachSection,
            CoverageDimension.EquationAlgorithmNotMixed,
            CoverageDimension.ConnectionsNotMixed,
        };

        Assert.Equal(CoverageDimension.Layout, skipped.Aggregate(CoverageDimension.None, (a, d) => a | d));

        // And each of them is a dimension whose rule the checker only runs when the class is not
        // excluded — asserted by running the checker both ways over a class that violates them.
        var settings = new StyleCheckingSettings
        {
            OneOfEachSection = true,
            ImportStatementsFirst = true,
            DontMixEquationAndAlgorithm = true,
            DontMixConnections = true,
            InitialEQAlgoFirst = true,
        };
        var definition = new ModelDefinition("A", ImportsLate);

        var reported = StyleChecking.RunStyleCheckingFindings(definition, settings, "A");
        var excluded = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("A", ImportsLate), settings, "A", isExcludedFromFormatting: true);

        Assert.NotEmpty(reported);
        Assert.Empty(excluded);
    }
}
