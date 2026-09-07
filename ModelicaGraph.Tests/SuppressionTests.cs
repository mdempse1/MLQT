using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.StyleRules;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>Phase 5a: __MLQT vendor-annotation suppression.</summary>
public class SuppressionTests
{
    private static List<Finding> Check(string code, StyleCheckingSettings settings, bool honor = true)
        => StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", code), settings, "TestModel", honorSuppressions: honor);

    private static StyleCheckingSettings ParamRule => new() { ParameterHasDescription = true };

    [Fact]
    public void ClassLevelSuppress_RemovesThatRuleForTheClass()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="Doc.ParameterDescription", reason="legacy"));
            end TestModel;
            """;
        Assert.Empty(Check(code, ParamRule));
    }

    [Fact]
    public void FullRuleId_AlsoMatches()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="MLQT.Doc.ParameterDescription"));
            end TestModel;
            """;
        Assert.Empty(Check(code, ParamRule));
    }

    [Fact]
    public void Wildcard_SuppressesEverythingForTheClass()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="*"));
            end TestModel;
            """;
        var settings = new StyleCheckingSettings { ParameterHasDescription = true, ClassHasDescription = true };
        Assert.Empty(Check(code, settings));
    }

    [Fact]
    public void ComponentLevelSuppress_AppliesToThatComponentOnly()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0 annotation(__MLQT(suppress="Doc.ParameterDescription"));
              parameter Real y = 2.0;
            end TestModel;
            """;
        var findings = Check(code, ParamRule);
        Assert.Single(findings);
        Assert.Equal("y", findings[0].ElementPath); // x suppressed, y still flagged
    }

    [Fact]
    public void UnrelatedRule_IsNotSuppressed()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="Naming.Convention"));
            end TestModel;
            """;
        Assert.Single(Check(code, ParamRule)); // suppresses a different rule, so the param finding stands
    }

    [Fact]
    public void Suppression_IsLineIndependent()
    {
        // Suppression keys on rule + element, not position — so it survives reformatting/line shifts.
        var code = """
            model TestModel


              parameter Real x = 1.0;

              annotation(__MLQT(suppress="Doc.ParameterDescription"));
            end TestModel;
            """;
        Assert.Empty(Check(code, ParamRule));
    }

    [Fact]
    public void PreserveOrder_SuppressesOrderingRules()
    {
        // An import after a component would normally flag ImportStatementsFirst; preserveOrder waives it.
        var code = """
            model TestModel
              Real x;
              import Modelica.Units.SI;
              annotation(__MLQT(preserveOrder=true, reason="order affects the nonlinear system"));
            end TestModel;
            """;
        Assert.Empty(Check(code, new StyleCheckingSettings { ImportStatementsFirst = true }));
    }

    // ---- the two formatting exclusions waive the same rules (B72) -----------------------------

    /// <summary>A class that breaks every rule the checker puts behind <c>isExcludedFromFormatting</c>.</summary>
    private const string BreaksEveryLayoutRule = """
        model TestModel
          Real x;
          import Modelica.Units.SI;
          extends Modelica.Icons.Example;
        equation
          connect(a.p, b.n);
          x = time;
        initial equation
          x = 0;
        algorithm
          x := x;
        end TestModel;
        """;

    private static StyleCheckingSettings EveryLayoutRule => new()
    {
        ImportStatementsFirst = true,
        OneOfEachSection = true,
        DontMixEquationAndAlgorithm = true,
        DontMixConnections = true,
        InitialEQAlgoFirst = true,
        InitialEQAlgoLast = true,
    };

    [Fact]
    public void TheTwoFormattingExclusionsWaiveTheSameRules()
    {
        // "Which rules are layout rules" is written in three places: the if (!isExcludedFromFormatting)
        // block in StyleChecking, MlqtSuppressionExtractor.FormattingRuleIds, and
        // CoverageDimension.Layout. CoverageDimensionsTests pins the first to the third. This pins the
        // first to the second, which nothing did — so an eighth layout rule added to the checker would
        // have been waived by the FormattingExcludedModels name list and not by __MLQT(format=false),
        // and every existing test would still have passed.
        var byNameList = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", BreaksEveryLayoutRule), EveryLayoutRule, "TestModel",
            isExcludedFromFormatting: true);

        var annotated = BreaksEveryLayoutRule.Replace(
            "end TestModel;", "annotation(__MLQT(format=false));\nend TestModel;");
        var byAnnotation = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", annotated), EveryLayoutRule, "TestModel");

        Assert.Equal(
            byNameList.Select(f => f.RuleId).OrderBy(id => id, StringComparer.Ordinal),
            byAnnotation.Select(f => f.RuleId).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void WithNeitherExclusion_TheSameClassReportsLayoutFindings()
    {
        // The test above passes vacuously if the fixture breaks no rule, which is exactly how it would
        // rot: a grammar or rule change that stops it reporting leaves two empty sets matching.
        var reported = StyleChecking.RunStyleCheckingFindings(
            new ModelDefinition("M", BreaksEveryLayoutRule), EveryLayoutRule, "TestModel");

        Assert.NotEmpty(reported);
    }

    [Fact]
    public void NoSuppress_IgnoresAnnotations()
    {
        var code = """
            model TestModel
              parameter Real x = 1.0;
              annotation(__MLQT(suppress="*"));
            end TestModel;
            """;
        Assert.NotEmpty(Check(code, ParamRule, honor: false));
    }

    // ---- one read per class, shared by everything that wants it (B55) --------------------------

    private const string Waiving = """
        model TestModel
          parameter Real x = 1.0;
          annotation(__MLQT(suppress="Doc.ParameterDescription"));
        end TestModel;
        """;

    [Fact]
    public void TheDirectivesAreReadOnceAndKeptOnTheClass()
    {
        // Three passes want this answer about the same class in the same run — the checker, the
        // coverage measurer and the graph analyses, the last of which used to re-parse to get it.
        var definition = new ModelDefinition("M", Waiving);

        var first = ClassSuppressions.For(definition, "TestModel");
        var second = ClassSuppressions.For(definition, "TestModel");

        Assert.Same(first, second);
        Assert.Same(first, definition.Suppressions);
        Assert.False(first.IsEmpty);
    }

    [Fact]
    public void AClassCarryingNothing_KeepsTheOneSharedEmptySet()
    {
        // Nearly every class carries nothing, and a library holds tens of thousands of them, so the
        // kept answer has to cost a reference rather than a set.
        var a = new ModelDefinition("A", "model A end A;");
        var b = new ModelDefinition("B", "model B end B;");

        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(a, "A"));
        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(b, "B"));
    }

    [Fact]
    public void EditingTheSourceDropsWhatWasReadFromTheOldOne()
    {
        var definition = new ModelDefinition("M", Waiving);
        Assert.False(ClassSuppressions.For(definition, "TestModel").IsEmpty);

        definition.ModelicaCode = "model TestModel\n  parameter Real x = 1.0;\nend TestModel;";

        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(definition, "TestModel"));
    }

    [Fact]
    public void AClassThatWillNotParse_CarriesNoDirectives()
    {
        // The safe direction: a broken file loses its waivers rather than silently gaining every one
        // of them. Its parse error is reported on its own account.
        var definition = new ModelDefinition("M", "model TestModel this is not Modelica");

        Assert.Same(SuppressionSet.Empty, ClassSuppressions.For(definition, "TestModel"));
    }

    [Fact]
    public void ReadingTheDirectivesDoesNotTakeATreeTheCallerWasHolding()
    {
        var definition = new ModelDefinition("M", Waiving);
        var tree = definition.EnsureParsed();

        ClassSuppressions.For(definition, "TestModel");

        Assert.Same(tree, definition.ParsedCode);
    }

    [Fact]
    public void ReadingThemForSomeoneElsesClassHandsTheTreeBack()
    {
        var definition = new ModelDefinition("M", Waiving);

        ClassSuppressions.For(definition, "TestModel");

        Assert.Null(definition.ParsedCode);
    }
}
