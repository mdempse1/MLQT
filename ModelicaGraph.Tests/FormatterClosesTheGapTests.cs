using System.Linq;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.StyleRules;
using ModelicaParser.Visitors;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// The claim <c>CoverageDimensions.FormatterRewrites</c> makes, checked against the formatter.
///
/// <para>Those five dimensions are dropped from the report when the formatter is on, on the grounds
/// that "reporting them would measure the moment before the save rather than the library". That is
/// only honest while the renderer really does satisfy each of those rules — and it has already been
/// wrong once: <c>InitialSectionsLast</c> was in the set while the renderer wrote initial sections
/// first whatever the setting said, so the report hid a gap the next save would reintroduce. Nothing
/// held the set to the renderer; the existing test asserts the constant against itself
/// (backlog B92).</para>
///
/// <para>The shape of each test is the round trip the claim describes: a class that breaks the rule,
/// rendered with the formatting the repository would have on, then checked again with the rule.</para>
/// </summary>
public class FormatterClosesTheGapTests
{
    private static string Render(string code, FormattingOptions formatting)
    {
        var tree = ModelicaParserHelper.Parse(code);
        var renderer = new ModelicaRenderer(
            renderForCodeEditor: false, showAnnotations: true, excludeClassDefinitions: false,
            tokenStream: null, classNamesToExclude: null, formatting: formatting);
        renderer.VisitStored_definition(tree);
        return string.Join("\n", renderer.Code);
    }

    /// <summary>The rule ids still reported after the class has been through the formatter.</summary>
    private static IReadOnlyList<string> StillReported(
        string code, StyleCheckingSettings settings)
    {
        var formatted = Render(code, settings.ToFormattingOptions());
        return StyleChecking
            .RunStyleCheckingFindings(new ModelDefinition("A", formatted), settings, "A")
            .Select(f => f.RuleId)
            .Distinct()
            .ToList();
    }

    /// <summary>Breaks import order, extends order, section order and initial-section order at once.</summary>
    private const string Untidy = """
        model A "a"
          Real x "state";
          import Modelica.Units.SI;
          extends Modelica.Icons.Example;
        protected
          Real p "protected";
        public
          Real y "more";
        initial equation
          x = 0;
        equation
          x = time;
        end A;
        """;

    private static StyleCheckingSettings WithFormatter(Action<StyleCheckingSettings> configure)
    {
        var settings = new StyleCheckingSettings { ApplyFormattingRules = true, OneOfEachSection = true };
        configure(settings);
        return settings;
    }

    [Fact]
    public void TheUntidyClassReallyDoesBreakTheseRules()
    {
        // Without this, every assertion below could pass on a fixture that breaks nothing.
        var settings = WithFormatter(s =>
        {
            s.ImportStatementsFirst = true;
            s.InitialEQAlgoFirst = true;
        });

        var reported = StyleChecking
            .RunStyleCheckingFindings(new ModelDefinition("A", Untidy), settings, "A")
            .Select(f => f.RuleId)
            .Distinct()
            .ToList();

        Assert.Contains(RuleIds.ImportStatementsFirst, reported);
        Assert.Contains(RuleIds.OneOfEachSection, reported);
    }

    [Theory]
    [InlineData(RuleIds.ImportStatementsFirst)]
    [InlineData(RuleIds.ExtendsAtTop)]
    [InlineData(RuleIds.OneOfEachSection)]
    public void TheFormatterSatisfiesTheRuleItIsCreditedWith(string ruleId)
    {
        var settings = WithFormatter(s => s.ImportStatementsFirst = true);

        Assert.DoesNotContain(ruleId, StillReported(Untidy, settings));
    }

    [Fact]
    public void InitialSectionsFirst_IsSatisfiedByTheFormatter()
    {
        var settings = WithFormatter(s => s.InitialEQAlgoFirst = true);

        Assert.DoesNotContain(RuleIds.InitialEqAlgoFirst, StillReported(Untidy, settings));
    }

    [Fact]
    public void InitialSectionsLast_IsSatisfiedByTheFormatter()
    {
        // The one that was credited to the formatter before the renderer honoured it.
        var settings = WithFormatter(s => s.InitialEQAlgoLast = true);

        Assert.DoesNotContain(RuleIds.InitialEqAlgoLast, StillReported(Untidy, settings));
    }

    [Fact]
    public void EveryDimensionCreditedToTheFormatterHasATestAbove()
    {
        // The list is the claim. If a sixth dimension joins FormatterRewrites, this fails until
        // somebody has shown the formatter closes that gap too.
        var covered = new[]
        {
            CoverageDimension.ImportsFirst,
            CoverageDimension.ExtendsAtTop,
            CoverageDimension.OneOfEachSection,
            CoverageDimension.InitialSectionsFirst,
            CoverageDimension.InitialSectionsLast,
        }.Aggregate(CoverageDimension.None, (a, d) => a | d);

        // Reached through TrackedFor, which is the only public way to see the set: with the formatter
        // on, exactly these drop out of the tracked layout dimensions. Asked twice because the two
        // initial-section rules are mutually exclusive — a configuration can credit the formatter with
        // one or the other, never both, so neither run alone sees the whole set.
        Assert.Equal(covered, DroppedByTheFormatter(initialLast: false) | DroppedByTheFormatter(initialLast: true));
    }

    /// <summary>Which tracked dimensions switching the formatter on takes off the report.</summary>
    private static CoverageDimension DroppedByTheFormatter(bool initialLast)
    {
        StyleCheckingSettings Configured() => new()
        {
            OneOfEachSection = true,
            ImportStatementsFirst = true,
            InitialEQAlgoFirst = !initialLast,
            InitialEQAlgoLast = initialLast,
            DontMixEquationAndAlgorithm = true,
            DontMixConnections = true,
        };

        var withoutFormatter = CoverageDimensions.TrackedFor(Configured());
        var withFormatter = Configured();
        withFormatter.ApplyFormattingRules = true;

        return withoutFormatter & ~CoverageDimensions.TrackedFor(withFormatter);
    }

    [Fact]
    public void TheTwoTheFormatterCannotFix_StayOnTheReport()
    {
        // Mixing connections with equations, and mixing equation with algorithm, are not layout the
        // renderer rearranges — so they are deliberately not credited to it, and the report keeps them.
        var settings = new StyleCheckingSettings
        {
            ApplyFormattingRules = true,
            OneOfEachSection = true,
            DontMixConnections = true,
            DontMixEquationAndAlgorithm = true,
        };

        var tracked = CoverageDimensions.TrackedFor(settings);

        Assert.True(tracked.HasFlag(CoverageDimension.ConnectionsNotMixed));
        Assert.True(tracked.HasFlag(CoverageDimension.EquationAlgorithmNotMixed));
    }
}
