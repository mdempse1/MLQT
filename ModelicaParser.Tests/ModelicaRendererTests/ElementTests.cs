using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class ElementTests
{
    /// <summary>
    /// Helper method to test a single line of Modelica code - component declarations.
    /// </summary>
    /// <param name="expectedLine">Input Modelica code in expected formating</param>
    /// <param name="renderForCodeEditor">Whether to render with markup tags</param>
    private void AssertElementList(string expectedLine, bool renderForCodeEditor = false)
    {
        var testModel = "model Test\n" + expectedLine + "\nend Test;";
        var parseTree = ModelicaParserHelper.Parse(testModel);
        var visitor = new ModelicaRenderer(renderForCodeEditor);
        visitor.Visit(parseTree);

        // Remove trailing empty lines from actual output
        var actualOutput = visitor.Code.ToList();
        while (actualOutput.Count > 0 && string.IsNullOrEmpty(actualOutput[actualOutput.Count - 1]))
        {
            actualOutput.RemoveAt(actualOutput.Count - 1);
        }
        actualOutput.RemoveAt(0); // Remove "model Test" line
        actualOutput.RemoveAt(actualOutput.Count - 1); // Remove "end Test;" line

        // Check the specified line index
        Assert.Equal(expectedLine, actualOutput[0]);
    }


    [Fact]
    public void SimpleRealVariable_FormatsCorrectly()
    {
        var expectedLine = "  Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void RealVariableWithInitialValue_FormatsCorrectly()
    {
        var expectedLine = "  Real x=1.0;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void RealVariableWithAssignment_FormatsCorrectly()
    {
        var expectedLine = "  Real x := 1.0;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void RealVariableWithModification_FormatsCorrectly()
    {
        var expectedLine = "  Real x(start=1.0);";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void RealVariableWithMultipleModification_FormatsCorrectly()
    {
        var expectedOutput = @"
model Test
  parameter Real x(start=1.0, 
    min=0.0, 
    max=100.0, 
    fixed=true, 
    stateSelect=StateSelect.prefer) ""Variable"";
end Test;";

        TestHelpers.AssertClass(expectedOutput);
    }

    [Fact]
    public void RealVariableWithDescription_FormatsCorrectly()
    {
        var expectedLine = "  Real x \"description here\";";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void RealVariableWithAnnotation_FormatsCorrectly()
    {
        var expectedLine = """
          model Test
            Real x 
              annotation (Evaluate=false);
          end Test;
          """;
        TestHelpers.AssertClass(expectedLine);
    }

    [Fact]
    public void ParameterVariable_FormatsCorrectly()
    {
        var expectedLine = "  parameter Real k=2.5;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ParameterVariableWithAll_FormatsCorrectly()
    {
        var expectedLine = """
            model Test
              parameter Real k(fixed=false)=2.5 "description"
                annotation (Evaluate=true);
            end Test;
            """;
        TestHelpers.AssertClass(expectedLine);
    }

    [Fact]
    public void MultipleVariables_FormatsCorrectly()
    {
        var expectedLine = "  Real x, y, z;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ArrayVariableFixedSize_FormatsCorrectly()
    {
        var expectedLine = "  Real x[3];";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ArrayVariableUnknownSize_FormatsCorrectly()
    {
        var expectedLine = "  Real x[:];";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ArrayVariableFixedSizeonType_FormatsCorrectly()
    {
        var expectedLine = "  Real[3] x;";
        AssertElementList(expectedLine);
    }


    [Fact]
    public void ArrayVariableUnknownSizeonType_FormatsCorrectly()
    {
        var expectedLine = "  Real[:] x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ArrayVariableFixedSize2D_FormatsCorrectly()
    {
        var expectedLine = "  Real x[3, 5];";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ArrayVariableUnknownSize2D_FormatsCorrectly()
    {
        var expectedLine = "  Real x[:, :];";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void PublicVariable_FormatsCorrectly()
    {
        var testModel = """ 
        model Test
        public
          Real y;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }    

    [Fact]
    public void ProtectedVariable_FormatsCorrectly()
    {
        var testModel = """ 
        model Test
        protected
          Real y;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }    

    [Fact]
    public void PublicProtectedVariable_FormatsCorrectly()
    {
        var testModel = """ 
        model Test
        public
          Real x;
        protected
          Real y;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }       

    [Fact]
    public void ExternalCodeSimple_FormatsCorrectly()
    {
        var testModel = """
        model Test
        external "C" externalFunction(x);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }      

    [Fact]
    public void ExternalCodeReturnValue_FormatsCorrectly()
    {
        var testModel = """ 
        model Test
        external "C" y = externalFunction(x);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }      

    [Fact]
    public void ExternalCodeComplexCall_FormatsCorrectly()
    {
        var testModel = """ 
        function Test
        external "C" der_y = ModelicaStandardTables_CombiTimeTable_getDerValue(tableID, icol, timeIn, nextTimeEvent, pre_nextTimeEvent, der_timeIn)
          annotation (
            IncludeDirectory="modelica://Modelica/Resources/C-Sources", 
            Include="#include \"ModelicaStandardTables.h\"", 
            Library={"ModelicaStandardTables", "ModelicaIO", "ModelicaMatIO", "zlib"}
          );
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ConstantVariable_FormatsCorrectly()
    {
        var expectedLine = "  constant Real pi=3.14159;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void DiscreteVariable_FormatsCorrectly()
    {
        var expectedLine = "  discrete Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void StreamVariable_FormatsCorrectly()
    {
        var expectedLine = "  stream Real h_outflow;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void AbsoluteTypeSpecifier_FormatsCorrectly()
    {
        var testModel = """
        model Test
          .Modelica.SIunits.Voltage v;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    #region Element Modifier Combinations (inner/outer/final/redeclare)

    [Fact]
    public void InnerVariable_FormatsCorrectly()
    {
        var expectedLine = "  inner Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void OuterVariable_FormatsCorrectly()
    {
        var expectedLine = "  outer Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void InnerOuterVariable_FormatsCorrectly()
    {
        var expectedLine = "  inner outer Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void FinalVariable_FormatsCorrectly()
    {
        var expectedLine = "  final Real x=1.0;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void RedeclareFinalVariable_FormatsCorrectly()
    {
        var expectedLine = "  redeclare final Real x=2.0;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void InnerModel_FormatsCorrectly()
    {
        var testModel = """
        model Outer

          inner model M
            Real x;
          end M;
        end Outer;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void OuterModel_FormatsCorrectly()
    {
        var testModel = """
        model Container

          outer model M
            Real x;
          end M;
        end Container;
        """;
        TestHelpers.AssertClass(testModel);
    }

    #endregion

    #region Modification Break (modification_expression with break)

    [Fact]
    public void ModificationBreak_FormatsCorrectly()
    {
        var testModel = """
        model Test
          extends Base(x=break);
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    #endregion

    #region Type Prefix Combinations

    [Fact]
    public void FlowInputVariable_FormatsCorrectly()
    {
        var expectedLine = "  flow input Real q;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void StreamInputVariable_FormatsCorrectly()
    {
        var expectedLine = "  stream input Real h;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void DiscreteInputVariable_FormatsCorrectly()
    {
        var expectedLine = "  discrete input Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ParameterInputVariable_FormatsCorrectly()
    {
        var expectedLine = "  parameter input Real x;";
        AssertElementList(expectedLine);
    }

    [Fact]
    public void ConstantOutputVariable_FormatsCorrectly()
    {
        var expectedLine = "  constant output Real x=1.0;";
        AssertElementList(expectedLine);
    }

    #endregion

    #region Conditional Component

    [Fact]
    public void ConditionalComponent_FormatsCorrectly()
    {
        var testModel = """
        model Test
          Real x if useX;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    #endregion

    #region Import Clause Variants

    [Fact]
    public void ImportWildcard_FormatsCorrectly()
    {
        var testModel = """
        model Test
          import Modelica.Math.*;
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    [Fact]
    public void ImportList_FormatsCorrectly()
    {
        var testModel = """
        model Test
          import Modelica.Math.{sin, cos, tan};
        end Test;
        """;
        TestHelpers.AssertClass(testModel);
    }

    #endregion
}
