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
        var settings = new StyleCheckingSettings { ImportStatementsFirst = true };

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
        var settings = new StyleCheckingSettings { InitialEQAlgoLast = true };

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
}
