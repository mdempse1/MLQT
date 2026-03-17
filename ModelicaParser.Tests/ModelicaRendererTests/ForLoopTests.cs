using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class ForLoopTests
{
#region For Loop Statements
    [Fact]
    public void ForLoopDotNotationStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          for i in 1:10 loop
            y[i] := 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopDotNotationWithStepStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          for i in 1:2:10 loop
            y[i] := 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopVectorStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          for i in {1, 2, 3, 5} loop
            y[i] := 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopExpressionStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          for i in 1:np - 1 loop
            y[i] := 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopExpressionBracketsStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm 
          for i in 1:(np - 1) loop
            y[i] := 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }
#endregion

#region For Loop Equations
    [Fact]
    public void ForLoopDotNotationEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          for i in 1:10 loop
            y[i] = 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopDotNotationWithStepEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          for i in 1:2:10 loop
            y[i] = 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopVectorEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          for i in {1, 2, 3, 5} loop
            y[i] = 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopExpressionEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test

        equation 
          for i in 1:np - 1 loop
            y[i] = 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ForLoopExpressionBracketsEquation_FormatsCorrectly()
    {
        var testModel = """
        model Test
        
        equation 
          for i in 1:(np - 1) loop
            y[i] = 1; 
          end for; 
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }
#endregion
}
