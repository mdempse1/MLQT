using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class EquationTests
{
    /// <summary>
    /// Helper method to test a single line of Modelica code - equations.
    /// </summary>
    /// <param name="expectedLine">Input Modelica code in expected formating</param>
    /// <param name="renderForCodeEditor">Whether to render with markup tags</param>
    private void AssertEquation(string expectedLine, bool renderForCodeEditor = false)
    {
        var testModel = "within;\nmodel Test\n\nequation\n" + expectedLine + "\nend Test;";
        var parseTree = ModelicaParserHelper.Parse(testModel);
        var visitor = new ModelicaRenderer(renderForCodeEditor);
        visitor.Visit(parseTree);

        // Remove trailing empty lines from actual output
        var actualOutput = visitor.Code.ToList();
        while (actualOutput.Count > 0 && string.IsNullOrEmpty(actualOutput[actualOutput.Count - 1]))
        {
            actualOutput.RemoveAt(actualOutput.Count - 1);
        }
        actualOutput.RemoveAt(3); // Remove "equation" line
        actualOutput.RemoveAt(2); // Remove empty line
        actualOutput.RemoveAt(1); // Remove "model Test" line
        actualOutput.RemoveAt(0); // Remove "within" line
        actualOutput.RemoveAt(actualOutput.Count - 1); // Remove "end Test;" line

        // Check the specified line index
        Assert.Equal(expectedLine, actualOutput[0]);
    }

    [Fact]
    public void SimpleEquation_FormatsCorrectly()
    {
        var expectedLine = "  x = 5;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ScalarPower_FormatsCorrectly()
    {
        var expectedLine = "  x = a.^5;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void Power_FormatsCorrectly()
    {
        var expectedLine = "  x = a^5;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void Equation_FormatsCorrectly()
    {
        var expectedLine = "  a + b = c + d;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void DerEquation_FormatsCorrectly()
    {
        var expectedLine = "  der(x) = -x;";
        AssertEquation(expectedLine);
    }


    [Fact]
    public void DerAlgorithmFromFunction_FormatsCorrectly()
    {
        var expectedLine = "  der(x) = f(a, y);";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void DerAlgorithmFromFunctionWithComment_FormatsCorrectly()
    {
        var expectedLine = "  der(x) = f(a, y) \"a comment\";";
        AssertEquation(expectedLine);
    } 

    [Fact]
    public void ArithmeticEquation_FormatsCorrectly()
    {
        var expectedLine = "  x = a + b*3;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ConnectClause_FormatsCorrectly()
    {
        var expectedLine = "  connect(a, b);";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ConnectClauseWithComment_FormatsCorrectly()
    {
        var expectedLine = "  connect(a, b) \"A comment\";";
        AssertEquation(expectedLine);
    }


    [Fact]
    public void AdditionExpression_FormatsCorrectly()
    {
        var expectedLine = "  x = a + b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void MultiplicationExpression_FormatsCorrectly()
    {
        var expectedLine = "  x = a*b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void PowerExpression_FormatsCorrectly()
    {
        var expectedLine = "  x = a^2;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void UnaryMinusExpression_FormatsCorrectly()
    {
        var expectedLine = "  x = -y;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void FunctionCallOneArgument_FormatsCorrectly()
    {
        var expectedLine = "  x = f(x);";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void FunctionCallOneArgumentWithComment_FormatsCorrectly()
    {
        var expectedLine = "  x = f(x) \"A comment\";";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void FunctionCallTwoArgument_FormatsCorrectly()
    {
        var expectedLine = "  x = f(x, y);";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void FunctionCallNamedArgument_FormatsCorrectly()
    {
        var expectedLine = "  x = f(x, y=y);";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void BracketedTerm_FormatsCorrectly()
    {
        var expectedLine = "  x = a + (b*3 + 5);";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void IndexExpression1_FormatsCorrectly()
    {
        var expectedLine = "  x = y[i + 1];";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void IndexExpression2_FormatsCorrectly()
    {
        var expectedLine = "  x = y[i + 2*t];";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void IndexExpression3_FormatsCorrectly()
    {
        var expectedLine = "  x[j] = y[3*i + 1];";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void IndexExpression4_FormatsCorrectly()
    {
        var expectedLine = "  x = y[i + 1]*z[j];";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void PurePrimary_FormatsCorrectly()
    {
        var expectedLine = "  a = pure(a, b, c);";
        AssertEquation(expectedLine);
    }  

    [Fact]
    public void PrimaryEnd_FormatsCorrectly()
    {
        var expectedLine = "  a = end;";
        AssertEquation(expectedLine);
    }      

    [Fact]
    public void FunctionForIndices_FormatsCorrectly()
    {
        var expectedLine = "  a = f(i for i in 1:10);";
        AssertEquation(expectedLine);
    }  

    [Fact]
    public void FunctionPartialApplication_FormatsCorrectly()
    {
        var expectedLine = "  a = f(function f(a=1, b=2));";
        AssertEquation(expectedLine);
    }      

    [Fact]
    public void FunctionPartialApplication2_FormatsCorrectly()
    {
        var expectedLine = "  a = f(x, function f(a=1, b=2));";
        AssertEquation(expectedLine);
    }      

    [Fact]
    public void ArrayArguments_FormatsCorrectly()
    {
        var expectedLine = "  a = {a, b, c};";
        AssertEquation(expectedLine);
    }       

    [Fact]
    public void ArrayArgumentsFor_FormatsCorrectly()
    {
        var expectedLine = "  a = {i for i in 1:10};";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ElementWiseAddition_FormatsCorrectly()
    {
        var expectedLine = "  x = a .+ b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ElementWiseSubtraction_FormatsCorrectly()
    {
        var expectedLine = "  x = a .- b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ElementWiseMultiplication_FormatsCorrectly()
    {
        var expectedLine = "  x = a .* b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void ElementWiseDivision_FormatsCorrectly()
    {
        var expectedLine = "  x = a ./ b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void NotEqualOperator_FormatsCorrectly()
    {
        var expectedLine = "  flag = a <> b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void InitialExpression_FormatsCorrectly()
    {
        var expectedLine = "  flag = initial();";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void AbsoluteComponentReference_FormatsCorrectly()
    {
        var expectedLine = "  x = .Modelica.Constants.pi;";
        AssertEquation(expectedLine);
    }

    #region Function Call as Equation

    [Fact]
    public void FunctionCallEquation_FormatsCorrectly()
    {
        var expectedLine = "  assert(x > 0, \"must be positive\");";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void FunctionCallEquationDotted_FormatsCorrectly()
    {
        var expectedLine = "  Connections.root(a.frame);";
        AssertEquation(expectedLine);
    }

    #endregion

    #region Matrix Constructor

    [Fact]
    public void MatrixConstructorMultiRow_FormatsCorrectly()
    {
        var expectedLine = "  A = [1, 2; 3, 4; 5, 6];";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void MatrixConstructorSingleRow_FormatsCorrectly()
    {
        var expectedLine = "  A = [1, 2, 3];";
        AssertEquation(expectedLine);
    }

    #endregion

    #region Logical Operators

    [Fact]
    public void LogicalAndExpression_FormatsCorrectly()
    {
        var expectedLine = "  flag = a > 0 and b > 0;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void LogicalOrExpression_FormatsCorrectly()
    {
        var expectedLine = "  flag = a > 0 or b > 0;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void LogicalNotExpression_FormatsCorrectly()
    {
        var expectedLine = "  flag = not a;";
        AssertEquation(expectedLine);
    }

    #endregion

    #region Relational Operators

    [Fact]
    public void LessThanOperator_FormatsCorrectly()
    {
        var expectedLine = "  flag = a < b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void LessThanOrEqualOperator_FormatsCorrectly()
    {
        var expectedLine = "  flag = a <= b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void GreaterThanOperator_FormatsCorrectly()
    {
        var expectedLine = "  flag = a > b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void GreaterThanOrEqualOperator_FormatsCorrectly()
    {
        var expectedLine = "  flag = a >= b;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void EqualityOperator_FormatsCorrectly()
    {
        var expectedLine = "  flag = a == b;";
        AssertEquation(expectedLine);
    }

    #endregion

    #region Range Expressions

    [Fact]
    public void RangeExpression_FormatsCorrectly()
    {
        var expectedLine = "  x = 1:10;";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void RangeExpressionWithStep_FormatsCorrectly()
    {
        var expectedLine = "  x = 1:2:10;";
        AssertEquation(expectedLine);
    }

    #endregion

    #region String Expressions

    [Fact]
    public void StringExpression_FormatsCorrectly()
    {
        var expectedLine = "  s = \"hello\";";
        AssertEquation(expectedLine);
    }

    [Fact]
    public void StringConcatenation_FormatsCorrectly()
    {
        var expectedLine = "  s = \"hello\" + \" world\";";
        AssertEquation(expectedLine);
    }

    #endregion

    #region Division Operator

    [Fact]
    public void DivisionExpression_FormatsCorrectly()
    {
        var expectedLine = "  x = a/b;";
        AssertEquation(expectedLine);
    }

    #endregion
}
