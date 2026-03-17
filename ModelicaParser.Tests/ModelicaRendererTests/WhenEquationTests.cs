using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class WhenEquationTests
{
#region When Statements
    [Fact]
    public void WhenExpressionStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when x > 0 then
            y := 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenElseWhenExpressionStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when x > 0 then
            y := 1;
          elsewhen x < 0 then
            y := -1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenExpressionMultipleStatements_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when x > 0 then
            y := 1; 
            z := 2;
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenElseWhenExpressionMultipleStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when x > 0 then
            y := 1;
            z := 2;
          elsewhen x < 0 then
            y := -1; 
            z := -2;
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenExpressionVectorConditionsStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when {x > 0, x > 1} then
            y := 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenExpressionFunctionCallStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when sample(0, 2) then
            y := 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenInitialExpression_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          when initial() then
            y := 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }    
#endregion

#region When Equations
  [Fact]
    public void WhenExpressionEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          when x > 0 then
            y = 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenElseWhenExpressionEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          when x > 0 then
            y = 1;
          elsewhen x < 0 then
            y = -1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

  [Fact]
    public void WhenExpressionMultipleEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          when x > 0 then
            y = 1; 
            z = 2;
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenElseWhenExpressionMultipleEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          when x > 0 then
            y = 1;
            z = 2;
          elsewhen x < 0 then
            y = -1; 
            z = -2;
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenExpressionVectorConditionsEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          when {x > 0, x > 1} then
            y = 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenExpressionFunctionCallEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test
        
        equation 
          when sample(0, 2) then
            y = 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void WhenInitialEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          when initial() then
            y = 1; 
          end when; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }    
#endregion
}
