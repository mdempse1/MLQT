using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class ReplaceableTests
{
#region Replaceable Statements
    [Fact]
    public void ReplaceableModel_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelModifier_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model(a=3);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelDescription_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model "Replacable model";
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelDescriptionAnnotation_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model "Replacable model" 
            annotation (
              Dialog(
                tab="Assumptions", 
                group="Heat transfer",
                enable=use_HeatTransfer
              ),
              choicesAllMatching=true
            );
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelConstrainingClause_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model 
            constrainedby Modelica.Model2;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelConstrainingClauseDescription_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model 
            constrainedby Modelica.Model2 "Test";
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelConstrainingClauseDescriptionAnnotation_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model 
            constrainedby Modelica.Model2 "Test"
            annotation (
              Dialog(
                tab="Assumptions", 
                group="Heat transfer",
                enable=use_HeatTransfer
              ),
              choicesAllMatching=true
            );
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelConstrainingClauseModifier_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model 
            constrainedby Modelica.Model2(a=3);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableModelAll_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable model HeatTransfer = Modelica.Model 
            constrainedby Modelica.Model2(a=3) "Replacable model" 
            annotation (
              Dialog(
                tab="Assumptions", 
                group="Heat transfer",
                enable=use_HeatTransfer
              ),
              choicesAllMatching=true
            );
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableClass_FormatsCorrectly()
    {
        var testModel = """
        package TestPackage
          replaceable record ThermodynamicState "Minimal variable set that is available as input argument to every medium function"
            extends Modelica.Icons.Record;
          end ThermodynamicState;
        end TestPackage;
        """;
        TestHelpers.AssertClass(testModel);
    }
#endregion

#region Replaceable Component with Constraint

    [Fact]
    public void ReplaceableComponent_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable Real x
            constrainedby Real;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReplaceableComponentModified_FormatsCorrectly()
    {
        var testModel = """
        model Test
          replaceable Real x=1.0
            constrainedby Real;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

#endregion
}