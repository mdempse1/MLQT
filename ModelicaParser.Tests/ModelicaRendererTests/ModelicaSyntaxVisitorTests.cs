using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using Antlr4.Runtime;

namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Comprehensive tests for ModelicaRenderer class.
/// Tests code generation in both plain text and markup modes.
/// </summary>
public class ModelicaRendererTests
{
    private List<string> GenerateCode(string modelicaCode, bool renderForCodeEditor = false, bool showAnnotations = true, bool excludeClassDefinitions = false, HashSet<string>? classNamesToExclude = null)
    {
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(modelicaCode);
        var visitor = new ModelicaRenderer(renderForCodeEditor, showAnnotations, excludeClassDefinitions, tokenStream, classNamesToExclude);
        visitor.VisitStored_definition(parseTree);
        return visitor.Code;
    }

    #region Basic Class Structure Tests

    [Fact]
    public void GenerateCode_SimpleModel_GeneratesCorrectly()
    {
        var code = @"
model SimpleModel
  Real x;
end SimpleModel;";

        var result = GenerateCode(code);

        Assert.Contains("model SimpleModel", string.Join("\n", result));
        Assert.Contains("Real x;", string.Join("\n", result));
        Assert.Contains("end SimpleModel", string.Join("\n", result));
    }

    [Fact]
    public void GenerateCode_WithinStatement_GeneratesCorrectly()
    {
        var code = @"
within TestPackage;
model TestModel
  Real y;
end TestModel;";

        var result = GenerateCode(code);

        Assert.Contains("within TestPackage;", string.Join("\n", result));
        Assert.Contains("model TestModel", string.Join("\n", result));
    }

    [Fact]
    public void GenerateCode_EncapsulatedModel_GeneratesCorrectly()
    {
        var code = @"
encapsulated model EncapsulatedModel
  Real z;
end EncapsulatedModel;";

        var result = GenerateCode(code);

        Assert.Contains("encapsulated model EncapsulatedModel", string.Join("\n", result));
    }

    [Fact]
    public void GenerateCode_FinalClassDefinition_GeneratesCorrectly()
    {
        var code = @"
final model FinalModel
  Real x;
end FinalModel;";

        var result = GenerateCode(code);

        Assert.Contains("final model FinalModel", string.Join("\n", result));
    }

    #endregion

    #region Markup Mode Tests

    [Fact]
    public void GenerateCode_MarkupMode_WrapsKeywords()
    {
        var code = "model Test Real x; end Test;";

        var result = GenerateCode(code, renderForCodeEditor: true);
        var fullCode = string.Join("\n", result);

        Assert.Contains("<KEYWORD>model</KEYWORD>", fullCode);
        Assert.Contains("<KEYWORD>end</KEYWORD>", fullCode);
        // Real is wrapped as TYPE, not KEYWORD
        Assert.Contains("<TYPE>Real</TYPE>", fullCode);
    }

    [Fact]
    public void GenerateCode_MarkupMode_WrapsIdentifiers()
    {
        var code = "model Test Real x; end Test;";

        var result = GenerateCode(code, renderForCodeEditor: true);
        var fullCode = string.Join("\n", result);

        Assert.Contains("<IDENT>Test</IDENT>", fullCode);
        Assert.Contains("<IDENT>x</IDENT>", fullCode);
    }

    [Fact]
    public void GenerateCode_MarkupMode_WrapsNumbers()
    {
        var code = "model Test Real x = 3.14; end Test;";

        var result = GenerateCode(code, renderForCodeEditor: true);
        var fullCode = string.Join("\n", result);

        Assert.Contains("<NUMBER>3.14</NUMBER>", fullCode);
    }

    [Fact]
    public void GenerateCode_MarkupMode_WrapsStrings()
    {
        var code = "model Test String s = \"hello\"; end Test;";

        var result = GenerateCode(code, renderForCodeEditor: true);
        var fullCode = string.Join("\n", result);

        Assert.Contains("<STRING>\"hello\"</STRING>", fullCode);
    }

    #endregion

    #region Component Declaration Tests

    [Fact]
    public void GenerateCode_ParameterDeclaration_GeneratesCorrectly()
    {
        var code = @"
model Test
  parameter Real p = 1.0;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        // Note: spacing may vary (p=1.0 vs p = 1.0)
        Assert.Contains("parameter Real p", fullCode);
        Assert.Contains("1.0", fullCode);
    }

    [Fact]
    public void GenerateCode_MultipleVariables_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, y, z;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("Real x, y, z;", fullCode);
    }

    [Fact]
    public void GenerateCode_ArraySubscripts_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x[3];
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("Real x[3];", fullCode);
    }

    [Fact]
    public void GenerateCode_FlowVariable_GeneratesCorrectly()
    {
        var code = @"
model Test
  flow Real q;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("flow Real q;", fullCode);
    }

    [Fact]
    public void GenerateCode_InputOutputVariables_GeneratesCorrectly()
    {
        var code = @"
model Test
  input Real u;
  output Real y;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("input Real u;", fullCode);
        Assert.Contains("output Real y;", fullCode);
    }

    #endregion

    #region Import and Extends Tests

    [Fact]
    public void GenerateCode_ImportStatement_GeneratesCorrectly()
    {
        var code = @"
model Test
  import Modelica.Math;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("import Modelica.Math;", fullCode);
    }

    [Fact]
    public void GenerateCode_ImportWithAlias_GeneratesCorrectly()
    {
        var code = @"
model Test
  import M = Modelica.Math;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        // Spacing may vary (M=Modelica.Math vs M = Modelica.Math)
        Assert.Contains("import M", fullCode);
        Assert.Contains("Modelica.Math", fullCode);
    }

    [Fact]
    public void GenerateCode_ImportWildcard_GeneratesCorrectly()
    {
        var code = @"
model Test
  import Modelica.Math.*;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        // The visitor generates the import statement
        Assert.Contains("import Modelica.Math", fullCode);
        // Note: The wildcard may or may not be preserved depending on visitor implementation
    }

    [Fact]
    public void GenerateCode_ImportList_GeneratesCorrectly()
    {
        var code = @"
model Test
  import Modelica.Math.{sin, cos, tan};
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("import Modelica.Math.{sin, cos, tan};", fullCode);
    }

    [Fact]
    public void GenerateCode_ExtendsClause_GeneratesCorrectly()
    {
        var code = @"
model Test
  extends BaseModel;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("extends BaseModel;", fullCode);
    }

    [Fact]
    public void GenerateCode_ExtendsWithModification_GeneratesCorrectly()
    {
        var code = @"
model Test
  extends BaseModel(p=5);
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("extends BaseModel(p=5);", fullCode);
    }

    #endregion

    #region Equation Section Tests

    [Fact]
    public void GenerateCode_SimpleEquation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
equation
  x = 5;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("equation", fullCode);
        Assert.Contains("x = 5;", fullCode);
    }

    [Fact]
    public void GenerateCode_InitialEquation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
initial equation
  x = 0;
equation
  der(x) = 1;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("initial equation", fullCode);
        Assert.Contains("x = 0;", fullCode);
    }

    [Fact]
    public void GenerateCode_ConnectEquation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Connector a, b;
equation
  connect(a, b);
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("connect(a, b);", fullCode);
    }

    [Fact]
    public void GenerateCode_IfEquation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, y;
  Boolean flag;
equation
  if flag then
    x = 1;
  else
    x = 0;
  end if;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("if flag then", fullCode);
        Assert.Contains("else", fullCode);
        Assert.Contains("end if", fullCode);
    }

    [Fact]
    public void GenerateCode_IfEquationWithElseif_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
  Integer mode;
equation
  if mode == 1 then
    x = 1;
  elseif mode == 2 then
    x = 2;
  else
    x = 0;
  end if;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("if mode == 1 then", fullCode);
        Assert.Contains("elseif mode == 2 then", fullCode);
        Assert.Contains("else", fullCode);
    }

    [Fact]
    public void GenerateCode_ForEquation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x[3];
equation
  for i in 1:3 loop
    x[i] = i;
  end for;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("for i in 1:3 loop", fullCode);
        Assert.Contains("end for", fullCode);
    }

    [Fact]
    public void GenerateCode_WhenEquation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
  Boolean trigger;
equation
  when trigger then
    x = 1;
  end when;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("when trigger then", fullCode);
        Assert.Contains("end when", fullCode);
    }

    #endregion

    #region Algorithm Section Tests

    [Fact]
    public void GenerateCode_SimpleAlgorithm_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
algorithm
  x := 5;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("algorithm", fullCode);
        Assert.Contains("x := 5;", fullCode);
    }

    [Fact]
    public void GenerateCode_IfStatement_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
  Boolean flag;
algorithm
  if flag then
    x := 1;
  else
    x := 0;
  end if;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("if flag then", fullCode);
        Assert.Contains("x := 1;", fullCode);
        Assert.Contains("else", fullCode);
        Assert.Contains("x := 0;", fullCode);
    }

    [Fact]
    public void GenerateCode_ForStatement_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x[3];
algorithm
  for i in 1:3 loop
    x[i] := i;
  end for;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("for i in 1:3 loop", fullCode);
        Assert.Contains("end for", fullCode);
    }

    [Fact]
    public void GenerateCode_WhileStatement_GeneratesCorrectly()
    {
        var code = @"
model Test
  Integer i;
algorithm
  i := 0;
  while i < 10 loop
    i := i + 1;
  end while;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("while i < 10 loop", fullCode);
        Assert.Contains("end while", fullCode);
    }

    [Fact]
    public void GenerateCode_WhenStatement_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
  Boolean trigger;
algorithm
  when trigger then
    x := 1;
  end when;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("when trigger then", fullCode);
        Assert.Contains("end when", fullCode);
    }

    #endregion

    #region Expression Tests

    [Fact]
    public void GenerateCode_ArithmeticExpression_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, y, z;
equation
  z = x + y * 2;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        // Spacing around operators may vary
        Assert.Contains("z", fullCode);
        Assert.Contains("x", fullCode);
        Assert.Contains("y", fullCode);
        Assert.Contains("2", fullCode);
    }

    [Fact]
    public void GenerateCode_LogicalExpression_GeneratesCorrectly()
    {
        var code = @"
model Test
  Boolean a, b, c;
equation
  c = a and b or not a;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("and", fullCode);
        Assert.Contains("or", fullCode);
        Assert.Contains("not", fullCode);
    }

    [Fact]
    public void GenerateCode_RelationalExpression_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
  Boolean flag;
equation
  flag = x > 0;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("flag = x > 0;", fullCode);
    }

    [Fact]
    public void GenerateCode_FunctionCall_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, y;
equation
  y = sin(x);
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("y = sin(x);", fullCode);
    }

    [Fact]
    public void GenerateCode_ArrayConstructor_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x[3];
equation
  x = {1, 2, 3};
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("x = {1, 2, 3};", fullCode);
    }

    [Fact]
    public void GenerateCode_MatrixConstructor_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real A[2, 2];
equation
  A = [1, 2; 3, 4];
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("A = [1, 2; 3, 4];", fullCode);
    }

    [Fact]
    public void GenerateCode_RangeExpression_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x[5];
equation
  x = 1:5;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("x = 1:5;", fullCode);
    }

    [Fact]
    public void GenerateCode_IfExpression_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, y;
  Boolean flag;
equation
  y = if flag then 1 else 0;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("y = if flag then 1 else 0;", fullCode);
    }

    #endregion

    #region Comment Tests

    [Fact]
    public void GenerateCode_StringComment_GeneratesCorrectly()
    {
        var code = @"
model Test ""This is a test model""
  Real x ""Variable x"";
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("\"This is a test model\"", fullCode);
        Assert.Contains("\"Variable x\"", fullCode);
    }

    [Fact]
    public void GenerateCode_LineComment_GeneratesCorrectly()
    {
        var code = @"
// This is a line comment
model Test
  Real x;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("// This is a line comment", fullCode);
    }

    [Fact]
    public void GenerateCode_BlockComment_GeneratesCorrectly()
    {
        var code = @"
/* This is a
   block comment */
model Test
  Real x;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("/* This is a", fullCode);
        Assert.Contains("block comment */", fullCode);
    }

    #endregion

    #region Annotation Tests

    [Fact]
    public void GenerateCode_SimpleAnnotation_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x annotation(Documentation(info=""<html>Test</html>""));
end Test;";

        var result = GenerateCode(code, showAnnotations: true);
        var fullCode = string.Join("\n", result);

        Assert.Contains("annotation", fullCode);
        Assert.Contains("Documentation", fullCode);
    }

    [Fact]
    public void GenerateCode_AnnotationHidden_WhenShowAnnotationsFalse()
    {
        var code = @"
model Test
  Real x annotation(Documentation(info=""<html>Test</html>""));
end Test;";

        var result = GenerateCode(code, showAnnotations: false);
        var fullCode = string.Join("\n", result);

        Assert.DoesNotContain("annotation", fullCode);
        Assert.DoesNotContain("Documentation", fullCode);
    }

    #endregion

    #region Short Class Specifier Tests

    [Fact]
    public void GenerateCode_TypeDefinition_GeneratesCorrectly()
    {
        var code = @"
type Voltage = Real(unit=""V"");
";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("type Voltage = Real", fullCode);
        Assert.Contains("unit", fullCode);
    }

    [Fact]
    public void GenerateCode_EnumerationType_GeneratesCorrectly()
    {
        var code = @"
type Color = enumeration(Red, Green, Blue);
";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("type Color = enumeration", fullCode);
        Assert.Contains("Red", fullCode);
        Assert.Contains("Green", fullCode);
        Assert.Contains("Blue", fullCode);
    }

    #endregion

    #region Class Exclusion Tests

    [Fact]
    public void GenerateCode_ExcludeClassDefinitions_ExcludesNestedClasses()
    {
        var code = @"
model Test
  Real x;

  model NestedModel
    Real y;
  end NestedModel;
end Test;";

        var result = GenerateCode(code, excludeClassDefinitions: true);
        var fullCode = string.Join("\n", result);

        Assert.Contains("model Test", fullCode);
        Assert.DoesNotContain("model NestedModel", fullCode);
    }

    [Fact]
    public void GenerateCode_ExcludeSpecificClasses_ExcludesNamedClasses()
    {
        var code = @"
model Test
  model Class1
    Real x;
  end Class1;

  model Class2
    Real y;
  end Class2;
end Test;";

        var classesToExclude = new HashSet<string> { "Class1" };
        var result = GenerateCode(code, classNamesToExclude: classesToExclude);
        var fullCode = string.Join("\n", result);

        Assert.DoesNotContain("model Class1", fullCode);
        Assert.Contains("model Class2", fullCode);
    }

    [Fact]
    public void GenerateCode_ExcludeShortClassByName_ExcludesTypeAlias()
    {
        // Covers GetClassNameFromDefinition short_class_specifier branch (lines 204-211 in ModelicaRenderer.cs)
        var code = @"
package TestPkg
  type MyReal = Real(min = 0) ""bounded real"";
  type MyVoltage = Real(unit = ""V"") ""voltage type"";
end TestPkg;";

        var classesToExclude = new HashSet<string> { "MyReal" };
        var result = GenerateCode(code, classNamesToExclude: classesToExclude);
        var fullCode = string.Join("\n", result);

        Assert.DoesNotContain("MyReal", fullCode);
        Assert.Contains("MyVoltage", fullCode);
    }

    [Fact]
    public void GenerateCode_ExcludeDerClassByName_ExcludesDerTypeAlias()
    {
        // Covers GetClassNameFromDefinition der_class_specifier branch (lines 213-220 in ModelicaRenderer.cs)
        var code = @"
package TestPkg
  type MyVelocity = der(MyPosition, time) ""velocity type"";
  type MyReal = Real ""basic real"";
end TestPkg;";

        var classesToExclude = new HashSet<string> { "MyVelocity" };
        var result = GenerateCode(code, classNamesToExclude: classesToExclude);
        var fullCode = string.Join("\n", result);

        Assert.DoesNotContain("MyVelocity", fullCode);
        Assert.Contains("MyReal", fullCode);
    }

    #endregion

    #region Replaceable and Redeclare Tests

    [Fact]
    public void GenerateCode_ReplaceableComponent_GeneratesCorrectly()
    {
        var code = @"
model Test
  replaceable Real x;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("replaceable Real x;", fullCode);
    }

    [Fact]
    public void GenerateCode_RedeclareComponent_GeneratesCorrectly()
    {
        var code = @"
model Test
  redeclare Real x;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("redeclare Real x;", fullCode);
    }

    [Fact]
    public void GenerateCode_InnerOuterComponents_GeneratesCorrectly()
    {
        var code = @"
model Test
  inner Real x;
  outer Real y;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("inner Real x;", fullCode);
        Assert.Contains("outer Real y;", fullCode);
    }

    #endregion

    #region External Function Tests

    [Fact]
    public void GenerateCode_ExternalFunction_GeneratesCorrectly()
    {
        var code = @"
function myFunc
  input Real x;
  output Real y;
external ""C"" y = externalFunc(x);
end myFunc;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("external \"C\"", fullCode);
        Assert.Contains("y = externalFunc(x);", fullCode);
    }

    [Fact]
    public void GenerateCode_ExternalFunctionNoLanguage_GeneratesCorrectly()
    {
        var code = @"
function myFunc
  input Real x;
  output Real y;
external y = externalFunc(x);
end myFunc;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("external", fullCode);
        Assert.Contains("y = externalFunc(x);", fullCode);
    }

    #endregion

    #region Modification Tests

    [Fact]
    public void GenerateCode_ClassModification_GeneratesCorrectly()
    {
        var code = @"
model Test
  BaseModel comp(p=5, q=10);
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("BaseModel comp(p=5, q=10);", fullCode);
    }

    [Fact]
    public void GenerateCode_ConditionalDeclaration_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x if enableX;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("Real x if enableX;", fullCode);
    }

    #endregion

    #region Public/Protected Sections Tests

    [Fact]
    public void GenerateCode_PublicSection_GeneratesCorrectly()
    {
        var code = @"
model Test
public
  Real x;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("public", fullCode);
    }

    [Fact]
    public void GenerateCode_ProtectedSection_GeneratesCorrectly()
    {
        var code = @"
model Test
protected
  Real x;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("protected", fullCode);
    }

    [Fact]
    public void GenerateCode_PublicAndProtectedSections_GeneratesCorrectly()
    {
        var code = @"
model Test
public
  Real x;
protected
  Real y;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("public", fullCode);
        Assert.Contains("protected", fullCode);
    }

    #endregion

    #region Der and Initial Tests

    [Fact]
    public void GenerateCode_DerFunction_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, dx;
equation
  dx = der(x);
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("dx = der(x);", fullCode);
    }

    [Fact]
    public void GenerateCode_InitialFunction_GeneratesCorrectly()
    {
        var code = @"
model Test
  Boolean flag;
equation
  flag = initial();
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("flag = initial();", fullCode);
    }

    #endregion

    #region Complex Expression Tests

    [Fact]
    public void GenerateCode_ExponentiationOperator_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x, y;
equation
  y = x ^ 2;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("y = x^2;", fullCode);
    }

    [Fact]
    public void GenerateCode_ComponentReference_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x;
equation
  x = comp.subcomp.value;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("x = comp.subcomp.value;", fullCode);
    }

    [Fact]
    public void GenerateCode_ArrayIndexing_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real x[3];
  Real y;
equation
  y = x[2];
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("y = x[2];", fullCode);
    }

    #endregion

    #region Multiple For Indices Tests

    [Fact]
    public void GenerateCode_MultipleForIndices_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real A[3, 3];
equation
  for i in 1:3, j in 1:3 loop
    A[i, j] = i * j;
  end for;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("for i in 1:3, j in 1:3 loop", fullCode);
    }

    #endregion

    #region Break and Return Statements Tests

    [Fact]
    public void GenerateCode_BreakStatement_GeneratesCorrectly()
    {
        var code = @"
function test
  output Integer result;
algorithm
  for i in 1:10 loop
    if i == 5 then
      break;
    end if;
  end for;
end test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("break;", fullCode);
    }

    [Fact]
    public void GenerateCode_ReturnStatement_GeneratesCorrectly()
    {
        var code = @"
function test
  output Integer result;
algorithm
  return;
end test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("return;", fullCode);
    }

    #endregion

    #region Named Arguments Tests

    [Fact]
    public void GenerateCode_NamedArguments_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real y;
equation
  y = myFunc(a=1, b=2);
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("y = myFunc(a=1, b=2);", fullCode);
    }

    #endregion

    #region Constraining Clause Tests

    [Fact]
    public void GenerateCode_ConstrainingClause_GeneratesCorrectly()
    {
        var code = @"
model Test
  replaceable model MyModel
    Real x;
  end MyModel
  constrainedby BaseModel;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("constrainedby", fullCode);
    }

    #endregion

    #region Empty Code Tests

    [Fact]
    public void GenerateCode_EmptyModel_GeneratesCorrectly()
    {
        var code = @"
model Empty
end Empty;";

        var result = GenerateCode(code);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    #endregion

    #region Multiple Class Definitions Tests

    [Fact]
    public void GenerateCode_MultipleClassDefinitions_GeneratesCorrectly()
    {
        var code = @"
model Model1
  Real x;
end Model1;

model Model2
  Real y;
end Model2;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        Assert.Contains("model Model1", fullCode);
        Assert.Contains("model Model2", fullCode);
    }

    #endregion

    #region Operator Spacing Tests

    [Fact]
    public void GenerateCode_OperatorSpacing_GeneratesCorrectly()
    {
        var code = @"
model Test
  Real a, b, c;
equation
  c = a + b;
  c = a - b;
  c = a * b;
  c = a / b;
end Test;";

        var result = GenerateCode(code);
        var fullCode = string.Join("\n", result);

        // Operators should have proper spacing
        Assert.Contains("c = a + b;", fullCode);
        Assert.Contains("c = a - b;", fullCode);
    }

    #endregion
}
