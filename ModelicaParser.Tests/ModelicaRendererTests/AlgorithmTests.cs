using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class AlgorithmTests
{
    /// <summary>
    /// Helper method to test a single line of Modelica code - algorithms.
    /// </summary>
    /// <param name="expectedLine">Input Modelica code in expected formating</param>
    /// <param name="renderForCodeEditor">Whether to render with markup tags</param>
    private void AssertAlgorithm(string expectedLine, bool renderForCodeEditor = false)
    {
        var testModel = "within;\nmodel Test\n\nalgorithm\n" + expectedLine + "\nend Test;";
        var parseTree = ModelicaParserHelper.Parse(testModel);
        var visitor = new ModelicaRenderer(renderForCodeEditor);
        visitor.Visit(parseTree);

        // Remove trailing empty lines from actual output
        var actualOutput = visitor.Code.ToList();
        while (actualOutput.Count > 0 && string.IsNullOrEmpty(actualOutput[actualOutput.Count - 1]))
        {
            actualOutput.RemoveAt(actualOutput.Count - 1);
        }
        actualOutput.RemoveAt(3); // Remove "algorithm" line
        actualOutput.RemoveAt(2); // Remove empty line
        actualOutput.RemoveAt(1); // Remove "model Test" line
        actualOutput.RemoveAt(0); // Remove "within" line
        actualOutput.RemoveAt(actualOutput.Count - 1); // Remove "end Test;" line

        // Check the specified line index
        Assert.Equal(expectedLine, actualOutput[0]);
    }


    [Fact]
    public void SimpleAlgorithm_FormatsCorrectly()
    {
        var expectedLine = "  x := 5;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void SimpleAlgorithmAndComment_FormatsCorrectly()
    {
        var expectedLine = """
        model Test

        algorithm
          x := 5;
          //A c-style comment 
        end Test;
        """;
        TestHelpers.AssertClass(expectedLine);
    }

    [Fact]
    public void DerAlgorithm_FormatsCorrectly()
    {
        var expectedLine = "  der(x) := -x;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void DerAlgorithmFromFunction_FormatsCorrectly()
    {
        var expectedLine = "  der(x) := f(a, y);";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void DerAlgorithmFromFunctionWithComment_FormatsCorrectly()
    {
        var expectedLine = "  der(x) := f(a, y) \"a comment\";";
        AssertAlgorithm(expectedLine);
    } 

    [Fact]
    public void ArithmeticAlgorithm_FormatsCorrectly()
    {
        var expectedLine = "  x := a + b*3;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void AdditionExpression_FormatsCorrectly()
    {
        var expectedLine = "  x := a + b;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void MultiplicationExpression_FormatsCorrectly()
    {
        var expectedLine = "  x := a*b;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void PowerExpression_FormatsCorrectly()
    {
        var expectedLine = "  x := a^2;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void UnaryMinusExpression_FormatsCorrectly()
    {
        var expectedLine = "  x := -y;";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void FunctionCallOneArgument_FormatsCorrectly()
    {
        var expectedLine = "  x := f(x);";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void FunctionCallTwoArgument_FormatsCorrectly()
    {
        var expectedLine = "  x := f(x, y);";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void FunctionCallNamedArgument_FormatsCorrectly()
    {
        var expectedLine = "  x := f(x, y=y);";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void BracketedTerm_FormatsCorrectly()
    {
        var expectedLine = "  x := a + (b*3 + 5);";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void OutputExpressionList_FormatsCorrectly()
    {
        var expectedLine = "  (a, b, c) := F(a, b, c);";
        AssertAlgorithm(expectedLine);
    }    

    [Fact]
    public void FunctionCallNoOutputs_FormatsCorrectly()
    {
        var expectedLine = "  F(a, b, c);";
        AssertAlgorithm(expectedLine);
    }    

    [Fact]
    public void DerAssignment_FormatsCorrectly()
    {
        var expectedLine = "  der(a) := (a, b, c);";
        AssertAlgorithm(expectedLine);
    }    

    [Fact]
    public void PurePrimary_FormatsCorrectly()
    {
        var expectedLine = "  a := pure(a, b, c);";
        AssertAlgorithm(expectedLine);
    }  

    [Fact]
    public void PrimaryEnd_FormatsCorrectly()
    {
        var expectedLine = "  a := end;";
        AssertAlgorithm(expectedLine);
    }      

    [Fact]
    public void FunctionForIndices_FormatsCorrectly()
    {
        var expectedLine = "  a := f(i for i in 1:10);";
        AssertAlgorithm(expectedLine);
    }  

    [Fact]
    public void FunctionPartialApplication_FormatsCorrectly()
    {
        var expectedLine = "  a := f(function f(a=1, b=2));";
        AssertAlgorithm(expectedLine);
    }      

    [Fact]
    public void FunctionPartialApplication2_FormatsCorrectly()
    {
        var expectedLine = "  a := f(x, function f(a=1, b=2));";
        AssertAlgorithm(expectedLine);
    }      

    [Fact]
    public void ArrayArguments_FormatsCorrectly()
    {
        var expectedLine = "  a := {a, b, c};";
        AssertAlgorithm(expectedLine);
    }       

    [Fact]
    public void ArrayArgumentsFor_FormatsCorrectly()
    {
        var expectedLine = "  a := {i for i in 1:10};";
        AssertAlgorithm(expectedLine);
    }

    #region Statement Variants

    [Fact]
    public void BreakStatement_FormatsCorrectly()
    {
        var testModel = """
        model Test

        algorithm
          for i in 1:10 loop
            if x[i] > 0 then
              break;
            end if;
          end for;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ReturnStatement_FormatsCorrectly()
    {
        var testModel = """
        function Test
          output Real y;

        algorithm
          y := 1;
          return;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void OutputExpressionListEmpty_FormatsCorrectly()
    {
        var expectedLine = "  (x, , z) := func(a);";
        AssertAlgorithm(expectedLine);
    }

    [Fact]
    public void OutputExpressionWithArrayArgs_FormatsCorrectly()
    {
        var expectedLine = "  x := (func(a))[1];";
        AssertAlgorithm(expectedLine);
    }
    #endregion
}
