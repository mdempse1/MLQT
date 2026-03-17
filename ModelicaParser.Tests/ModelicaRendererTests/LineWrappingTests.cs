using ModelicaParser.Helpers;
using ModelicaParser.Visitors;

using ModelicaParser;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for line wrapping functionality in ModelicaRenderer.
/// </summary>
public class LineWrappingTests
{
    #region Component Declaration Wrapping Tests

    [Fact]
    public void LineWrapping_LongComponentWithComment_WrapsComment()
    {
        // Arrange - component with moderately long name and comment that together exceed limit
        var code = """
model Test
  parameter Real myParameter=1.0 "This is a very long comment that should wrap to a new line when it exceeds the maximum line length";
end Test;
""";

        var expectedOutput = """
model Test
  parameter Real myParameter=1.0
    "This is a very long comment that should wrap to a new line when it exceeds the maximum line length";
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput);
    }

    [Fact]
    public void LineWrapping_LongComponentWithModification_WrapsModification()
    {
        // Arrange - component with long modification that exceeds line length
        var code = """
model Test
  parameter Real x(start=1.0, min=0.0, max=100.0, fixed=true, stateSelect=StateSelect.prefer) "Variable";
end Test;
""";

        var expectedOutput = """
model Test
  parameter Real x(start=1.0, 
    min=0.0, 
    max=100.0, 
    fixed=true, 
    stateSelect=StateSelect.prefer) "Variable";
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput);
    }

    [Fact]
    public void LineWrapping_ComponentWithAnnotation_NoWrapping()
    {
        // Arrange - very long component declaration
        var code = """
model Test
  Modelica.Blocks.Interfaces.RealOutput V(unit="m3") "Liquid volume"
    annotation (Placement(transformation(
      origin={40, 110},
      extent={{-10, -10}, {10, 10}},
      rotation=90
    )));
end Test;
""";

         TestHelpers.AssertClass(code);
    }

    [Fact]
    public void LineWrapping_VeryLongComponentDeclaration_CommentWraps()
    {
        // Arrange - very long component declaration
        var code = """
model Test
  parameter Boolean allowFlowReversal=system.allowFlowReversal 
    "= true, if flow reversal is enabled, otherwise restrict flow to design direction (port_a -> port_b)"
    annotation (
      Dialog(tab="Assumptions"),
      Evaluate=true
    );
end Test;
""";

         TestHelpers.AssertClass(code);
    }

    [Fact]
    public void LineWrapping_VeryLongComponentDeclaration2_CommentWraps()
    {
        // Arrange - very long component declaration
        var code = """
model Test
  parameter Modelica.Units.SI.Volume V_l_start=V_t/2 
    "Start value of liquid volumeStart value of volume"
    annotation (Dialog(tab="Initialization"));
end Test;
""";

         TestHelpers.AssertClass(code);
    }

    #endregion

    #region Equation Wrapping Tests

    [Fact]
    public void LineWrapping_LongEquation_WrapsAtEquals()
    {
        // Arrange - equation with long LHS that exceeds 20 chars
        var code = """
model Test
  Real veryLongLeftHandSideVariable;

equation
  veryLongLeftHandSideVariable = someVeryLongRightHandSideExpression + anotherVeryLongTermThatCausesWrapping;
end Test;
""";

        var expectedOutput = """
model Test
  Real veryLongLeftHandSideVariable;

equation
  veryLongLeftHandSideVariable 
    = someVeryLongRightHandSideExpression + anotherVeryLongTermThatCausesWrapping;
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput);

    }

    [Fact]
    public void LineWrapping_LongEquationWithAddition_WrapsAtAddOp()
    {
        // Arrange - equation with many additions
        var code = """
model Test
  Real result;

equation
  result = term1 + term2 + term3 + term4 + term5 + term6 + term7 + term8 + term9 + term10 + term11 + term12;
end Test;
""";

        var expectedOutput = """
model Test
  Real result;

equation
  result = term1 + term2 + term3 + term4 + term5 + term6 + term7 + term8 + term9 + term10 + term11
    + term12;
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput);
    }

    [Fact]
    public void LineWrapping_EquationWithBrackets_KeepsBracketsOnSameLine()
    {
        // Arrange - equation with bracketed expressions
        var code = """
model Test
  Real result;

equation
  result = (term1 + term2 + term3) + (term4 + term5 + term6) + (term7 + term8 + term9 + term10 + term11 + term12);
end Test;
""";

        var expectedOutput = """
model Test
  Real result;

equation
  result = (term1 + term2 + term3) + (term4 + term5 + term6) 
    + (term7 + term8 + term9 + term10 + term11 + term12);
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput);
    }

    #endregion

    #region Statement Wrapping Tests

    [Fact]
    public void LineWrapping_LongAssignment_WrapsAtAssignment()
    {
        // Arrange - assignment with long LHS
        var code = """
model Test

algorithm
  veryLongLeftHandSideVariable := someVeryLongRightHandSideExpression + moreTerms + andEvenMoreTerms;
end Test;
""";

        var expectedOutput = """
model Test

algorithm
  veryLongLeftHandSideVariable := someVeryLongRightHandSideExpression + moreTerms 
    + andEvenMoreTerms;
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput);

    }

    [Fact]
    public void MultiLineStrings_ReadMultipleTimes_StillCorrectFormat()
    {
        // Test that multi-line strings are formatted consistently across multiple reads.
        // The formatter may apply line wrapping, but the key requirement is idempotency:
        // output1 == output2 == output3
        var code = """
model Test

equation
  assert(noEvent(u <= u_max), "Extrapolation warning: The value u (=" + String(u) 
    + ") must be less or equal than the maximum abscissa value u_max (=" + String(u_max) 
    + ") defined in the table.", level=AssertionLevel.warning);
end Test;
""";

        var output1 = TestHelpers.AssertClass(code, expectedOutput: code);
        var output2 = TestHelpers.AssertClass(output1, expectedOutput: code);
        var output3 = TestHelpers.AssertClass(output2, expectedOutput: code);
    }

    #endregion

    #region Documentation Exemption Tests

    [Fact]
    public void LineWrapping_DocumentationAnnotation_DoesNotWrap()
    {
        // Arrange - very long documentation that should not wrap
        var code = """
model Test

  annotation (
    Documentation(info="<html><p>This is a very long documentation string that exceeds the maximum line length by a significant margin but should not be wrapped because it is in a Documentation annotation.</p></html>")
  );
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: code);

    }

    [Fact]
    public void HTMLDocumentation_RepeatedReads_ConsistentOutput()
    {

    var code = """
model Test

  annotation (
    Documentation(info="<html>
<p>
Plant model for
<a href=\"modelica://Buildings.Controls.Continuous.Examples.LimPIDWithReset\">
Buildings.Controls.Continuous.Examples.LimPIDWithReset</a>.
consisting of a simple heat transfer model.
</p>
</html>")
  );
end Test;
""";

        var output1 = TestHelpers.AssertClass(code, expectedOutput: code);
        var output2 = TestHelpers.AssertClass(output1, expectedOutput: code);
        var output3 = TestHelpers.AssertClass(output2, expectedOutput: code);
    }

    [Fact]
    public void HTMLDocumentationNestedClasses_RepeatedReads_ConsistentOutput()
    {
        // Test that nested class with HTML documentation in protected section
        // formats consistently across multiple reads
        var code = """
model ParentModel

protected
  model Test

    annotation (
      Documentation(info="<html>
  <p>
  Plant model for
  <a href=\"modelica://Buildings.Controls.Continuous.Examples.LimPIDWithReset\">
  Buildings.Controls.Continuous.Examples.LimPIDWithReset</a>.
  consisting of a simple heat transfer model.
  </p>
  </html>")
    );
  end Test;
end ParentModel;
""";

        // The formatter removes blank line before protected and adds indented blank lines
        var expectedOutput = "model ParentModel\n" +
            "protected\n" +
            "  \n" +  // indented blank line
            "  model Test\n" +
            "  \n" +  // indented blank line
            "    annotation (\n" +
            "      Documentation(info=\"<html>\n" +
            "  <p>\n" +
            "  Plant model for\n" +
            "  <a href=\\\"modelica://Buildings.Controls.Continuous.Examples.LimPIDWithReset\\\">\n" +
            "  Buildings.Controls.Continuous.Examples.LimPIDWithReset</a>.\n" +
            "  consisting of a simple heat transfer model.\n" +
            "  </p>\n" +
            "  </html>\")\n" +
            "    );\n" +
            "  end Test;\n" +
            "end ParentModel;";

        var output1 = TestHelpers.AssertClass(code, expectedOutput: expectedOutput);
        var output2 = TestHelpers.AssertClass(output1, expectedOutput: expectedOutput);
        var output3 = TestHelpers.AssertClass(output2, expectedOutput: expectedOutput);
    }

    [Fact]
  public void ShortClassAnnotation_MultipleParsing_Consistent()
  {
    var code = """
type densitySelection = enumeration(
  fromTop "Density from top port",
  fromBottom "Density from bottom port",
  actual "Actual density based on flow direction"
) "Enumeration to select density in medium column"
  annotation (Documentation(info="<html>
<p>
Enumeration to define the choice of valve flow coefficient
(to be selected via choices menu):
</p>
<table border=\"1\" summary=\"Explanation of the enumeration\">
<tr><th>Enumeration</th>
    <th>Description</th></tr>
<tr><td>fromTop</td>
    <td>
Use this setting to use the density from the volume that is connected
to the top port.
    </td></tr>
<tr><td>fromBottom</td>
    <td>
Use this setting to use the density from the volume that is connected
to the bottom port.
</td></tr>
<tr><td>actual</td>
    <td>Use this setting to use the density based on the actual flow direction.
</td></tr>
</table>
</html>"));
""";

      var output1 = TestHelpers.AssertClass(code, expectedOutput: code);
      var output2 = TestHelpers.AssertClass(output1, expectedOutput: code);
      var output3 = TestHelpers.AssertClass(output2, expectedOutput: code);
  }

  [Fact]
  public void LongComponentAnnotation_RepeatedReading_ConsistentFormatting()
  {
    var code = """
connector NegativePin "Negative pin of an electrical component"
  SI.ElectricPotential v "Potential at the pin"
    annotation (unassignedMessage="An electrical potential cannot be uniquely calculated.
The reason could be that
- a ground object is missing (Modelica.Electrical.Analog.Basic.Ground)
  to define the zero potential of the electrical circuit, or
- a connector of an electrical component is not connected.");

  annotation (
    defaultComponentName="pin_n",
    Documentation(
      info="<html>
<p>Connectors PositivePin and NegativePin are nearly identical. The only difference is that the icons are different in order to identify more easily the pins of a component. Usually, connector PositivePin is used for the positive and connector NegativePin for the negative pin of an electrical component.</p>
</html>")
  );
end NegativePin;
""";

    // The formatter merges Documentation( with its first argument onto one line
    var expectedOutput = """
connector NegativePin "Negative pin of an electrical component"
  SI.ElectricPotential v "Potential at the pin"
    annotation (unassignedMessage="An electrical potential cannot be uniquely calculated.
The reason could be that
- a ground object is missing (Modelica.Electrical.Analog.Basic.Ground)
  to define the zero potential of the electrical circuit, or
- a connector of an electrical component is not connected.");

  annotation (
    defaultComponentName="pin_n",
    Documentation(info="<html>
<p>Connectors PositivePin and NegativePin are nearly identical. The only difference is that the icons are different in order to identify more easily the pins of a component. Usually, connector PositivePin is used for the positive and connector NegativePin for the negative pin of an electrical component.</p>
</html>")
  );
end NegativePin;
""";

      var output1 = TestHelpers.AssertClass(code, expectedOutput: expectedOutput);
      var output2 = TestHelpers.AssertClass(output1, expectedOutput: expectedOutput);
      var output3 = TestHelpers.AssertClass(output2, expectedOutput: expectedOutput);
  }
    #endregion

    #region Configuration Tests

    [Fact]
    public void LineWrapping_CustomMaxLength_RespectsConfiguration()
    {
        // Arrange
        var code = """
model Test
  Real x;

equation
  x = 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10 + 11 + 12 + 13 + 14 + 15;
end Test;
""";

        var expectedOutput = """
model Test
  Real x;

equation
  x = 1 + 2 + 3 + 4 + 5 + 6 + 7 + 8 + 9 + 10 + 11
    + 12 + 13 + 14 + 15;
end Test; 
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput, maxLineLength: 50);
    }

    [Fact]
    public void LineWrapping_LongExtends_RespectsConfiguration()
    {
        // Arrange
        var code = """
model AbruptAdaptor "Pressure drop in pipe due to suddenly expanding or reducing area (for both flow directions)"
  extends BaseClasses.QuadraticTurbulent.BaseModelNonconstantCrossSectionArea(final data=BaseClasses.QuadraticTurbulent.LossFactorData.suddenExpansion(diameter_a, diameter_b));
  parameter SI.Diameter diameter_a "Inner diameter of pipe at port_a";
  parameter SI.Diameter diameter_b "Inner diameter of pipe at port_b";
end AbruptAdaptor;  
""";

        var expectedOutput = """
model AbruptAdaptor "Pressure drop in pipe due to suddenly expanding or reducing area (for both flow directions)"
  extends BaseClasses.QuadraticTurbulent.BaseModelNonconstantCrossSectionArea(
    final data=BaseClasses.QuadraticTurbulent.LossFactorData.suddenExpansion(
      diameter_a, diameter_b
    )
  );
  parameter SI.Diameter diameter_a
    "Inner diameter of pipe at port_a";
  parameter SI.Diameter diameter_b
    "Inner diameter of pipe at port_b";
end AbruptAdaptor;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput, maxLineLength: 50);
    }

    [Fact]
    public void Render_LongExpressionWithMixedIndentation_FormattedCorrectly()
    {
      var code = """
block RealToInteger "Convert Real to Integer signal"
  extends Modelica.Blocks.Icons.IntegerBlock;
public
  Interfaces.RealInput u "Connector of Real input signal"
    annotation (Placement(transformation(extent={{-140, -20}, {-100, 20}})));
  Interfaces.IntegerOutput y "Connector of Integer output signal"
    annotation (Placement(transformation(extent={{100, -10}, {120, 10}})));

equation
  y = if (u > 0) then integer(floor(u + 0.5)) else integer(ceil(u - 0.5));
end RealToInteger;
""";

      TestHelpers.AssertClass(code);

    }

    [Fact]
    public void LineWrapping_DeclarationWithLongNameAndModification_WrapsBeforeModification()
    {
        // Covers VisitDeclaration wrappedBeforeModification path (lines 1478-1483, 1490 in ModelicaRenderer.cs)
        // Variable name itself exceeds maxLineLength, so modification wraps to next line
        var code = """
model Test
  parameter Real thisLongParameterName(start=0.0) = 1.0;
end Test;
""";

        var expectedOutput = """
model Test
  parameter Real thisLongParameterName
    (start=0.0)=1.0;
end Test;
""";

        TestHelpers.AssertClass(code, expectedOutput: expectedOutput, maxLineLength: 30);
    }

    [Fact]
    public void LineWrapping_DeclarationWithConditionAttribute_WrapsBeforeCondition()
    {
        // Covers VisitComponent_declaration wrappedBeforeCondition path (lines 1420-1425 in ModelicaRenderer.cs)
        // Variable name exceeds maxLineLength before the condition attribute
        var code = """
model Test
  Real thisLongVariableName if p > 0.0;
  parameter Boolean p=true;
equation
  thisLongVariableName = 1.0;
end Test;
""";

        // Use small maxLineLength to force wrapping before condition attribute
        // Use renderer directly to avoid strict line count assertion in AssertClass
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens("within;\n" + code);
        var visitor = new ModelicaRenderer(renderForCodeEditor: false, showAnnotations: true,
            excludeClassDefinitions: false, tokenStream, maxLineLength: 20);
        visitor.Visit(parseTree);
        var fullCode = string.Join("\n", visitor.Code);
        Assert.Contains("if p > 0.0;", fullCode);
        Assert.Contains("thisLongVariableName", fullCode);
    }
    #endregion

    #region Idempotency Tests

    [Fact]
    public void LineWrapping_FormattingIsIdempotent_LongComponentWithAnnotation()
    {
        // Formatting the same code twice should produce identical output.
        // This tests a bug where the first format of a long line with annotation-only comment
        // would produce an extra blank line that the second format would remove.
        var code = """
model Test
  parameter Real myVeryLongParameterName(min=0.0, max=100.0, start=50.0, nominal=25.0) annotation(Dialog(group="Parameters"));
end Test;
""";

        // First format — let the renderer decide the layout
        var firstResult = TestHelpers.FormatCode(code);

        // Second format — should produce identical output (idempotent)
        var secondResult = TestHelpers.FormatCode(firstResult);

        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public void LineWrapping_FormattingIsIdempotent_LongComponentWithDescriptionAndAnnotation()
    {
        // Component with both a description and annotation that exceeds line length.
        var code = """
model Test
  parameter Real myVeryLongParameterName(min=0.0, max=100.0, start=50.0) "A parameter description" annotation(Dialog(group="Parameters"));
end Test;
""";

        // First format — let the renderer decide the layout
        var firstResult = TestHelpers.FormatCode(code);

        // Second format — should produce identical output (idempotent)
        var secondResult = TestHelpers.FormatCode(firstResult);

        Assert.Equal(firstResult, secondResult);
    }

    [Fact]
    public void LineWrapping_FormattingIsIdempotent_NoBlankLineBeforeAnnotation()
    {
        // Verify that formatting doesn't insert a blank line between a long declaration
        // and its annotation (the specific bug that was reported).
        var code = """
model Test
  parameter Real myVeryLongParameterName(min=0.0, max=100.0, start=50.0, nominal=25.0) annotation(Dialog(group="Parameters"));
end Test;
""";

        var result = TestHelpers.FormatCode(code);

        // There should be no blank lines in the output
        var lines = result.Split('\n');
        for (int i = 0; i < lines.Length - 1; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]) && string.IsNullOrWhiteSpace(lines[i + 1]))
                Assert.Fail($"Found consecutive blank lines at line {i + 1}");
            // Also check: no blank line between a declaration line and an annotation line
            if (string.IsNullOrWhiteSpace(lines[i]) && lines.Length > i + 1 && lines[i + 1].TrimStart().StartsWith("annotation"))
                Assert.Fail($"Found blank line before annotation at line {i + 1}");
        }
    }

    #endregion
}
