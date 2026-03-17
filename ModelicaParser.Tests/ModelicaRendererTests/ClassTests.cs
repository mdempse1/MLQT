namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify single-line Modelica code formatting.
/// Each test parses a single line of Modelica code and verifies the formatted output.
/// </summary>
public class ClassTests
{
  //Test cases still needed for:
  // - replacable models
  // - conditional components
  // - redeclarations
  // - annotations
  // - external functions with annotations & class annotations

  [Fact]
  public void FunctionDeclaration_FormatsCorrectly()
  {
    var testModel = """ 
        function MyFunc
          output Real y;

        algorithm
          y := 1;
        end MyFunc;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void BlockDeclaration_FormatsCorrectly()
  {
    var testModel = """
        block MyBlock 
          Real x; 
        end MyBlock;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ImportStatement_FormatsCorrectly()
  {
    var testModel = """
        model Test
          import Modelica.Math; 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ImportStatementWithComment_FormatsCorrectly()
  {
    var testModel = """
        model Test
          import Modelica.Math "A comment"; 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ImportStatementAliasWithComment_FormatsCorrectly()
  {
    var testModel = """
        model Test
          import M = Modelica.Math "A comment"; 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClause_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel; 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsLongClassClause_FormatsCorrectly()
  {
    var testModel = """
        model extends Test(a=5) "Something"
          Real a;
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseOneModifier_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(a=5); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseWithAnnotation_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(a=5) 
            annotation (Evaluate=true); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseTwoModifiers_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(a=5, b=10); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseNestedModifier_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(InnerModel(c=3)); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseTwoNestedModifierA_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(InnerModel(c=3, b=4)); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseTwoNestedModifierB_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(InnerModel(c=3, SubModel(b=4))); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseTwoNestedModifierC_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(InnerModel(c=3), InnerModel2(b=4)); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseMoreNestedModifiers_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(
            InnerModel(
              SubModel(c=3), 
              SubModel2(AnotherSubModel(a=1))
            ), 
            InnerModel2(b=4)
          ); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseOneFinalModifier_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(final a=5); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExtendsClauseOneModifierWithComment_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(a=5 "something here"); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void Package_FormatsCorrectly()
  {
    var testModel = """
        package Test "Test file with multiple models including nested ones"

          model BaseModel "A simple base model"
            Real x(start=1.0);
            Real y;
            parameter Real k=0.5;

          equation 
            der(x) = -k*x;
            y = x;
          end BaseModel;

          model DerivedModel "Model that extends BaseModel"
            extends BaseModel;
            Real z;

          equation 
            z = 2*y;
          end DerivedModel;

          block ControlBlock "A control block"
            input Real u;
            output Real y;
            parameter Real gain=1.0;

          equation 
            y = gain*u;
          end ControlBlock;
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ShortClassDefinition1_FormatsCorrectly()
  {
    var testModel = """
        type PWMType = enumeration(
          SVPWM "SpaceVector PWM", 
          Intersective "Intersective PWM"
        ) "Enumeration defining the PWM type";        
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ShortClassDefinition2_FormatsCorrectly()
  {
    var testModel = """
        type PWMType = enumeration(
          First "First value", 
          Second "Second value",
          Third "Third value",
          Fourth "Fourth value"
        ) "Enumeration defining some options";        
        """;
    TestHelpers.AssertClass(testModel);
  }


  [Fact]
  public void MultiLineAnnotation_FormatsCorrectly()
  {
    var testModel = """
        model Test

          annotation (
            Icon(
              coordinateSystem(
                preserveAspectRatio=true,
                extent={{-100, -100}, {100, 100}}
              )
            ),
            Documentation(info="<html>
        <p>This very simple model provides a pressure drop which is proportional to the flowrate and to the <code>opening</code> input, without computing any fluid property. It can be used for testing purposes, when
        a simple model of a variable pressure loss is needed.</p>
        <p>A medium model must be nevertheless be specified, so that the fluid ports can be connected to other components using the same medium model.</p>
        <p>The model is adiabatic (no heat losses to the ambient) and neglects changes in kinetic energy from the inlet to the outlet.</p>
        </html>")
          );
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void MultiLineAnnotation_RendersCorrectly()
  {
    var testModel = """
        model Test

          annotation (
            Icon(
              coordinateSystem(
                preserveAspectRatio=true,
                extent={{-100, -100}, {100, 100}}
              )
            ),
            Documentation(info="<html>
        <p>This very simple model provides a pressure drop which is proportional to the flowrate and to the <code>opening</code> input, without computing any fluid property. It can be used for testing purposes, when
        a simple model of a variable pressure loss is needed.</p>
        <p>A medium model must be nevertheless be specified, so that the fluid ports can be connected to other components using the same medium model.</p>
        <p>The model is adiabatic (no heat losses to the ambient) and neglects changes in kinetic energy from the inlet to the outlet.</p>
        </html>")
          );
        end Test;
        """;

    var expectedOutput = """
        <KEYWORD>model</KEYWORD> <IDENT>Test</IDENT>

          <KEYWORD>annotation</KEYWORD> (
            <NAME>Icon</NAME>(
              <NAME>coordinateSystem</NAME>(
                <NAME>preserveAspectRatio</NAME><OPERATOR>=</OPERATOR><KEYWORD>true</KEYWORD>,
                <NAME>extent</NAME><OPERATOR>=</OPERATOR>{{<OPERATOR>-</OPERATOR><NUMBER>100</NUMBER>, <OPERATOR>-</OPERATOR><NUMBER>100</NUMBER>}, {<NUMBER>100</NUMBER>, <NUMBER>100</NUMBER>}}
              )
            ),
            <NAME>Documentation</NAME>(<NAME>info</NAME><OPERATOR>=</OPERATOR><STRING>"<html></STRING>
        <STRING><p>This very simple model provides a pressure drop which is proportional to the flowrate and to the <code>opening</code> input, without computing any fluid property. It can be used for testing purposes, when</STRING>
        <STRING>a simple model of a variable pressure loss is needed.</p></STRING>
        <STRING><p>A medium model must be nevertheless be specified, so that the fluid ports can be connected to other components using the same medium model.</p></STRING>
        <STRING><p>The model is adiabatic (no heat losses to the ambient) and neglects changes in kinetic energy from the inlet to the outlet.</p></STRING>
        <STRING></html>"</STRING>)
          );
        <KEYWORD>end</KEYWORD> <IDENT>Test</IDENT>;
        """;

    TestHelpers.AssertClass(testModel, true, expectedOutput);
  }

  [Fact]
  public void ComplexExtendsWithRedeclare_FormatsCorrectly()
  {
    var testModel = """
        partial model TestModel "Test model"
          extends Buildings.Fluid.DXSystems.Cooling.BaseClasses.PartialDXCoil(
        redeclare Buildings.Fluid.DXSystems.Cooling.BaseClasses.DXCooling
          dxCoi(redeclare final package Medium = Medium,
          replaceable Buildings.Fluid.DXSystems.Cooling.AirSource.Data.Generic.DXCoil datCoi),
        redeclare final Buildings.Fluid.MixingVolumes.MixingVolumeMoistAir vol(
          prescribedHeatFlowRate=true),
        replaceable Buildings.Fluid.DXSystems.Cooling.AirSource.Data.Generic.DXCoil datCoi);

        end TestModel;
        """;
    var expectedOutput = """
        partial model TestModel "Test model"
          extends Buildings.Fluid.DXSystems.Cooling.BaseClasses.PartialDXCoil(
            redeclare Buildings.Fluid.DXSystems.Cooling.BaseClasses.DXCooling dxCoi(
              redeclare final package Medium = Medium,
              replaceable Buildings.Fluid.DXSystems.Cooling.AirSource.Data.Generic.DXCoil datCoi
            ),
            redeclare final Buildings.Fluid.MixingVolumes.MixingVolumeMoistAir vol(prescribedHeatFlowRate=true),
            replaceable Buildings.Fluid.DXSystems.Cooling.AirSource.Data.Generic.DXCoil datCoi
          );
        end TestModel;
        """;

      TestHelpers.AssertClass(testModel, false, expectedOutput);
  }

  [Fact]
  public void InheritenceBreakConnect_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(break connect(a, b)); 
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void InheritenceBreakIdent_FormatsCorrectly()
  {
    var testModel = """
        model Test
          extends BaseModel(break TestThing);
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ClassKeyword_FormatsCorrectly()
  {
    var testModel = """
        class MyClass
          Real x;
        end MyClass;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void OperatorRecord_FormatsCorrectly()
  {
    var testModel = """
        operator record Complex
          Real re;
          Real im;
        end Complex;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ExpandableConnector_FormatsCorrectly()
  {
    var testModel = """
        expandable connector Bus
          Real x;
        end Bus;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PureFunction_FormatsCorrectly()
  {
    var testModel = """
        pure function Add
          input Real a;
          input Real b;
          output Real c;

        algorithm
          c := a + b;
        end Add;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ImpureFunction_FormatsCorrectly()
  {
    var testModel = """
        impure function WriteFile
          input String name;

        algorithm
        end WriteFile;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void OperatorFunction_FormatsCorrectly()
  {
    var testModel = """
        operator function '+'
          input Real a;
          input Real b;
          output Real c;

        algorithm
          c := a + b;
        end '+';
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void Operator_FormatsCorrectly()
  {
    var testModel = """
        operator 'Complex.*'
        end 'Complex.*';
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void ConnectorDeclaration_FormatsCorrectly()
  {
    var testModel = """
        connector Pin
          Real v;
          flow Real i;
        end Pin;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void RecordDeclaration_FormatsCorrectly()
  {
    var testModel = """
        record Point
          Real x;
          Real y;
        end Point;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void WithinEmpty_FormatsCorrectly()
  {
    var code = @"within;
model Test
  Real x;
end Test;";
    var (parseTree, tokenStream) = ModelicaParser.Helpers.ModelicaParserHelper.ParseWithTokens(code);
    var visitor = new ModelicaParser.Visitors.ModelicaRenderer(tokenStream: tokenStream);
    visitor.VisitStored_definition(parseTree);
    var fullCode = string.Join("\n", visitor.Code);
    Assert.Contains("within", fullCode);
    Assert.DoesNotContain("within Modelica", fullCode); // No package name
    Assert.Contains("model Test", fullCode);
  }

  #region Partial Class Prefix Variants

  [Fact]
  public void PartialClass_FormatsCorrectly()
  {
    var testModel = """
        partial class MyClass
          Real x;
        end MyClass;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PartialRecord_FormatsCorrectly()
  {
    var testModel = """
        partial record MyRecord
          Real x;
        end MyRecord;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PartialBlock_FormatsCorrectly()
  {
    var testModel = """
        partial block MyBlock
          Real x;
        end MyBlock;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PartialConnector_FormatsCorrectly()
  {
    var testModel = """
        partial connector MyConn
          Real x;
        end MyConn;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PartialPackage_FormatsCorrectly()
  {
    var testModel = """
        partial package MyPkg
        end MyPkg;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PartialFunction_FormatsCorrectly()
  {
    var testModel = """
        partial function MyFunc
          input Real x;
          output Real y;
        end MyFunc;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void PartialType_FormatsCorrectly()
  {
    var testModel = """
        partial type MyType = Real;
        """;
    TestHelpers.AssertClass(testModel);
  }

  #endregion

  #region Stored Definition Variants

  [Fact]
  public void FinalClassDefinition_FormatsCorrectly()
  {
    var code = @"within;
final model Test
  Real x;
end Test;";
    var (parseTree, tokenStream) = ModelicaParser.Helpers.ModelicaParserHelper.ParseWithTokens(code);
    var visitor = new ModelicaParser.Visitors.ModelicaRenderer(tokenStream: tokenStream);
    visitor.VisitStored_definition(parseTree);
    var fullCode = string.Join("\n", visitor.Code);
    Assert.Contains("final", fullCode);
    Assert.Contains("model Test", fullCode);
  }

  [Fact]
  public void MultipleClassDefinitions_FormatsCorrectly()
  {
    var code = @"within;
model A
  Real x;
end A;
model B
  Real y;
end B;";
    var (parseTree, tokenStream) = ModelicaParser.Helpers.ModelicaParserHelper.ParseWithTokens(code);
    var visitor = new ModelicaParser.Visitors.ModelicaRenderer(tokenStream: tokenStream);
    visitor.VisitStored_definition(parseTree);
    var fullCode = string.Join("\n", visitor.Code);
    Assert.Contains("model A", fullCode);
    Assert.Contains("end A;", fullCode);
    Assert.Contains("model B", fullCode);
    Assert.Contains("end B;", fullCode);
  }

  #endregion

  #region Long Class Specifier Extends Variants

  [Fact]
  public void ExtendsLongClassWithoutModification_FormatsCorrectly()
  {
    var testModel = """
        model extends Test "A description"
          Real a;
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void EncapsulatedClass_FormatsCorrectly()
  {
    var testModel = """
        encapsulated model Test
          Real x;
        end Test;
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void SimpleModel_FormatsCorrectly()
  {
    var testModel = """
model SimpleModel
  Real x;
  Real y;
  // Added variable

equation
  x = time;
  y = cos(x);

  annotation (Icon(graphics={Bitmap(extent={{-100, -102}, {100, 102}},
    fileName="modelica://ModelicaEditorTest/Resources/icon.png")}));
end SimpleModel;
""";

    TestHelpers.AssertClass(testModel);
  }
  #endregion
}
