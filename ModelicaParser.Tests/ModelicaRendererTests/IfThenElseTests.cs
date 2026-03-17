using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class IfThenElseTests
{
#region If Statements
    [Fact]
    public void IfStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          if x > 0 then 
            y := 1; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }


    [Fact]
    public void IfStatementFlag_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          if use_reset then 
            y := 1; 
          else
            y := -1;
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseIfStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          if x > 0 then 
            y := 1;
          elseif x < 0 then
            y := -1;
          else 
            y := 0; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfStatementNestedFlag_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          if use_reset then 
            if x > 0 then 
              y := 1;
            else 
              y := -1; 
            end if; 
          else
            y := -1;
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfInitialStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        initial algorithm 
          if x > 0 then 
            y := 1;
          else 
            y := -1; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseIfInitialStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        initial algorithm 
          if x > 0 then 
            y := 1;
          elseif x < 0 then 
            y := -1; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseExpressionStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          y := if x > 0 then 1 else -1;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseIfExpressionStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          y := if x > 0 then 1 elseif x < 0 then -1 else 0;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfStatementWithComment_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          if x > 0 then 
            y := 1; 
          end if "A comment"; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfStatementWithBreak_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          if x > 0 then 
            y := 1; 
            break "A comment";
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }
#endregion

#region If Equations
    [Fact]
    public void IfEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          if x > 0 then 
            y = 1;
          else 
            y = -1; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfEquationFlag_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          if use_reset then 
            y = 1; 
          else
            y = -1;
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseIfEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          if x > 0 then 
            y = 1;
          elseif x < 0 then
            y = -1;
          else 
            y = 0; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfEquationNestedFlag_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          if use_reset then 
            if x > 0 then 
              y = 1;
            else 
              y = -1; 
            end if; 
          else
            y = -1;
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfInitialEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        initial equation 
          if x > 0 then 
            y = 1;
          else 
            y = -1; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseIfInitialEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test
        
        initial equation 
          if x > 0 then 
            y = 1;
          elseif x < 0 then 
            y = -1; 
          end if; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseExpressionEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          y = if x > 0 then 1 else -1;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void IfElseIfExpressionEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          y = if x > 0 then 1 elseif x < 0 then -1 else 0;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

#endregion
}
