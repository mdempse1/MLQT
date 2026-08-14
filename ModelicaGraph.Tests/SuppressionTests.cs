using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
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
}
