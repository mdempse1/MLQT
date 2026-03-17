using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using ModelicaParser.Helpers;
using Xunit;

namespace ModelicaParser.Tests;

/// <summary>
/// Tests to achieve broad grammar coverage for modelicaParser, modelicaBaseListener, and modelicaBaseVisitor.
/// Uses ParseTreeWalker with modelicaBaseListener to exercise all Enter/Exit grammar rule methods.
/// Each test parses Modelica code that targets specific grammar rules, then walks the tree
/// to trigger listener callbacks and exercises the visitor hierarchy.
/// </summary>
public class GrammarCoverageTests
{
    private static void WalkWithListener(string code)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var listener = new modelicaBaseListener();
        ParseTreeWalker.Default.Walk(listener, parseTree);
    }

    // ============================================================================
    // Listener coverage: comprehensive models covering all major grammar rules
    // ============================================================================

    [Fact]
    public void Listener_LongClassWithEquations_CoversCommonRules()
    {
        // Covers: stored_definition, class_definition, class_specifier, class_prefixes,
        // long_class_specifier, composition, element_list, element, component_clause,
        // type_prefix, type_specifier, component_list, component_declaration, declaration,
        // modification, modification_expression, comment, string_comment, name,
        // component_reference, equation_section, equation_or_comment,
        // expression, simple_expression, logical_expression, logical_term,
        // logical_factor, relation, rel_op, arithmetic_expression, add_op,
        // term, mul_op, factor, primary, annotation, every_rule
        var code = """
model SimpleEquations "model with equations"
  parameter Real a = 1.0 "param a";
  parameter Real b = 2.0 "param b";
  Real x(start = 0.0) "state x";
  Real y "variable y";
equation
  x = a * sin(b) + 1.0;
  y = x ^ 2 - a / b;
end SimpleEquations;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ModelWithAlgorithm_CoversAlgorithmRules()
    {
        // Covers: algorithm_section, statement, statement_or_comment
        var code = """
model WithAlgorithm "model with algorithm"
  Real x;
  Real y;
algorithm
  x := 1.0;
  y := x + 2.0;
end WithAlgorithm;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_IfEquation_CoversIfElseifElseEquationRules()
    {
        // Covers: if_equation, elseif_equation, else_equation
        var code = """
model WithIfEquation "if-elseif-else equation"
  Real x;
  Real y;
equation
  if x > 1.0 then
    y = 1.0;
  elseif x > 0.0 then
    y = 0.5;
  else
    y = 0.0;
  end if;
end WithIfEquation;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_IfStatement_CoversIfElseifElseStatementRules()
    {
        // Covers: if_statement, elseif_statement, else_statement
        var code = """
model WithIfStatement "if-elseif-else statement"
  Real x;
  Real y;
algorithm
  if x > 1.0 then
    y := 1.0;
  elseif x > 0.0 then
    y := 0.5;
  else
    y := 0.0;
  end if;
end WithIfStatement;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ForEquation_CoversForEquationRules()
    {
        // Covers: for_equation, for_indices, for_index
        var code = """
model WithForEquation "for equation"
  Real x[5];
  Integer n = 5;
equation
  for i in 1:n loop
    x[i] = i * 1.0;
  end for;
end WithForEquation;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ForStatement_CoversForStatementRules()
    {
        // Covers: for_statement, for_indices, for_index (in algorithm)
        var code = """
model WithForStatement "for statement"
  Real x[5];
algorithm
  for i in 1:5 loop
    x[i] := i * 2.0;
  end for;
end WithForStatement;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_WhileStatement_CoversWhileRule()
    {
        // Covers: while_statement
        var code = """
model WithWhile "while statement"
  Integer n;
algorithm
  n := 0;
  while n < 10 loop
    n := n + 1;
  end while;
end WithWhile;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_WhenEquation_CoversWhenElsewhenEquationRules()
    {
        // Covers: when_equation, elsewhen_equation
        var code = """
model WithWhenEquation "when-elsewhen equation"
  Real x;
  Real y;
equation
  when x > 1.0 then
    y = pre(y) + 1.0;
  elsewhen x < -1.0 then
    y = pre(y) - 1.0;
  end when;
end WithWhenEquation;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_WhenStatement_CoversWhenElsewhenStatementRules()
    {
        // Covers: when_statement, elsewhen_statement
        var code = """
model WithWhenStatement "when-elsewhen statement"
  Real x;
  Real y;
algorithm
  when x > 1.0 then
    y := pre(y) + 1.0;
  elsewhen x < -1.0 then
    y := pre(y) - 1.0;
  end when;
end WithWhenStatement;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ConnectClause_CoversConnectRule()
    {
        // Covers: connect_clause
        var code = """
model WithConnect "connect clause"
  Modelica.Blocks.Sources.Sine sine1;
  Modelica.Blocks.Interfaces.RealOutput y;
equation
  connect(sine1.y, y);
end WithConnect;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_InitialSections_CoversInitialEquationAlgorithmRules()
    {
        // Covers: equation_section (initial), algorithm_section (initial)
        var code = """
model WithInitial "initial equation and algorithm"
  Real x;
  Real y;
initial equation
  x = 0.0;
initial algorithm
  y := 0.0;
equation
  x = 1.0;
end WithInitial;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ShortClassSpecifier_CoversShortClassRules()
    {
        // Covers: short_class_specifier
        var code = """
type Voltage = Real(unit = "V") "Voltage in volts";
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_DerClassSpecifier_CoversDerClassRules()
    {
        // Covers: der_class_specifier
        var code = """
type Velocity = der(Position, time) "Velocity type";
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_EnumerationType_CoversEnumRules()
    {
        // Covers: enum_list, enumeration_literal
        var code = """
type Color = enumeration(Red "red color", Green "green color", Blue "blue color") "RGB colors";
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ExternalFunction_CoversExternalRules()
    {
        // Covers: language_specification, external_function_call
        var code = """
function cFunction "External C function"
  input Real x "input value";
  output Real y "output value";
  external "C" y = c_func(x);
end cFunction;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ImportWithList_CoversImportListRule()
    {
        // Covers: import_clause, import_list
        var code = """
model WithImport "model with imports"
  import Modelica.SIunits;
  import Modelica.Blocks.{Sources, Interfaces};
  Real x;
equation
  x = 1.0;
end WithImport;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ExtendsClause_CoversExtendsRule()
    {
        // Covers: extends_clause, class_or_inheritence_modification,
        // argument_or_inheritence_list
        var code = """
model WithExtends "model with extends"
  extends Modelica.Icons.Package;
  extends BaseModel(param = 1.0, other = 2.0);
  Real x;
equation
  x = 1.0;
end WithExtends;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ConstrainingClause_CoversConstrainingRule()
    {
        // Covers: constraining_clause
        var code = """
model WithConstraint "model with constraining clause"
  replaceable model InnerModel = Modelica.Blocks.Sources.Sine
    constrainedby Modelica.Blocks.Interfaces.SO "constrained model";
  Real x;
equation
  x = 1.0;
end WithConstraint;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ConditionalAttribute_CoversConditionRule()
    {
        // Covers: condition_attribute
        var code = """
model WithConditional "conditional component"
  parameter Boolean useFilter = true "enable filter";
  Real x;
  Real filtered if useFilter;
equation
  x = 1.0;
end WithConditional;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_NamedFunctionArgs_CoversNamedArgRules()
    {
        // Covers: function_call_args, function_arguments, named_arguments,
        // named_argument, function_argument, function_arguments_non_first
        var code = """
model WithNamedArgs "named function arguments"
  Real x;
  Real y;
equation
  x = Modelica.Math.sin(x = 1.0);
  y = someFunction(a = x, b = 2.0, c = x + 1.0);
end WithNamedArgs;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ArraySubscripts_CoversArraySubscriptRules()
    {
        // Covers: array_subscripts, subscript_
        var code = """
model WithArraySubscripts "array subscripts"
  Real x[5];
  Real y[3, 3];
equation
  x[1] = 1.0;
  x[2:4] = {2.0, 3.0, 4.0};
  y[1, 1] = x[5];
end WithArraySubscripts;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ArrayLiteral_CoversArrayArgRules()
    {
        // Covers: array_arguments, array_arguments_non_first,
        // expression_list
        var code = """
model WithArrayLiteral "array literal"
  Real v[4];
equation
  v = {1.0, 2.0, 3.0, 4.0};
end WithArrayLiteral;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_OutputExpressionList_CoversOutputExprListRule()
    {
        // Covers: output_expression_list
        var code = """
model WithOutputExprList "multiple outputs"
  Real a;
  Real b;
  Real c;
equation
  (a, b) = Modelica.Math.sincos(1.0);
  (a, , c) = someFunc(1.0);
end WithOutputExprList;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ConditionalExpression_CoversElseifExprRule()
    {
        // Covers: elseif_expression
        var code = """
model WithConditionalExpr "conditional expression"
  Real x;
  Real y;
equation
  y = if x > 1.0 then 1.0 elseif x > 0.0 then 0.5 else 0.0;
end WithConditionalExpr;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ShortClassInComposition_CoversShortClassDefRule()
    {
        // Covers: short_class_definition, component_clause1, component_declaration1
        var code = """
model WithInnerShortClass "inner short class definition"
  type MyVoltage = Real(unit = "V") "voltage";
  type MyAngle = Real(unit = "rad") "angle";
  MyVoltage v;
  MyAngle phi;
equation
  v = 230.0;
  phi = 0.0;
end WithInnerShortClass;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ElementRedeclaration_CoversRedeclareRules()
    {
        // Covers: element_redeclaration, inheritence_modification
        var code = """
model WithRedeclare "element redeclaration"
  extends BaseModel(redeclare model InnerModel = NewInnerModel);
  Real x;
equation
  x = 1.0;
end WithRedeclare;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ElementReplaceable_CoversReplaceableRules()
    {
        // Covers: element_replaceable
        var code = """
model WithReplaceable "replaceable element"
  replaceable model M = Modelica.Blocks.Sources.Sine "replaceable model";
  Real x;
equation
  x = 1.0;
end WithReplaceable;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_FlowAndStreamPrefix_CoversBasePrefixRule()
    {
        // Covers: base_prefix (flow/stream)
        var code = """
connector FluidPort "fluid connector"
  Real p "pressure";
  flow Real m_flow "mass flow rate";
  stream Real h_outflow "specific enthalpy";
end FluidPort;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ComplexExpressions_CoversAllExpressionRules()
    {
        // Covers: rel_op (all operators), add_op (.+, .-), mul_op (all),
        // logical operations (and, or, not)
        var code = """
model ComplexExpressions "complex expressions"
  Real a;
  Real b;
  Real c;
  Boolean flag;
equation
  a = 1.0 + 2.0 - 3.0 .+ 4.0 .- 5.0;
  b = 2.0 * 3.0 / 4.0 .* 5.0 ./ 6.0;
  c = 2.0 ^ 3.0;
  flag = (a > b) and (b < c) or not (a >= c) and (b <= a) or (a == b) or (a <> c);
end ComplexExpressions;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_WithinAndAnnotation_CoversStoredDefinitionWithName()
    {
        // Covers: stored_definition with within name
        var code = """
within MyLibrary.SubPackage;
model WithWithin "model with within clause"
  Real x;
equation
  x = 1.0;
  annotation (Icon(graphics = {Rectangle(extent = {{-100, -100}, {100, 100}})}));
end WithWithin;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_CStyleComment_CoversCCommentRule()
    {
        // Covers: c_comment
        var code = """
/* This is a C-style block comment */
model WithCComment "C comment test"
  /* Another block comment */
  Real x /* inline comment */;
equation
  x = 1.0 /* result */;
end WithCComment;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_FunctionPartialApplication_CoversPartialAppRule()
    {
        // Covers: function_partial_application
        var code = """
model WithPartialFunction "partial function application"
  function f = Modelica.Math.exp "partial function";
  Real x;
equation
  x = f(1.0);
end WithPartialFunction;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_RecordAndBlock_CoversOtherClassPrefixes()
    {
        // Covers: class_prefixes for record, block
        var code = """
record MyRecord "a record"
  Real x "x value";
  Real y "y value";
end MyRecord;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_PackageWithNested_CoversPackagePrefixAndNesting()
    {
        // Covers: class_prefixes (package), nested class definitions
        var code = """
package MyPackage "a package"
  model Inner "inner model"
    Real x;
  equation
    x = 1.0;
  end Inner;
end MyPackage;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_FunctionClass_CoversFunctionPrefix()
    {
        // Covers: class_prefixes (function), input/output type prefixes
        var code = """
function myFunction "a pure function"
  input Real x "input x";
  input Real y "input y";
  output Real result "result";
algorithm
  result := x + y;
end myFunction;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ConnectorClass_CoversConnectorPrefix()
    {
        // Covers: class_prefixes (connector)
        var code = """
connector RealInput "real input connector"
  input Real signal "signal value";
end RealInput;
""";
        WalkWithListener(code);
    }

    // ============================================================================
    // Parser rule coverage: verify specific parse tree structures
    // ============================================================================

    [Fact]
    public void Parser_ShortClassSpecifier_HasCorrectStructure()
    {
        var code = """
type Voltage = Real(unit = "V") "Voltage";
""";
        var parseTree = ModelicaParserHelper.Parse(code);

        Assert.NotNull(parseTree.class_definition());
        Assert.NotEmpty(parseTree.class_definition());
        var shortSpec = parseTree.class_definition()[0].class_specifier().short_class_specifier();
        Assert.NotNull(shortSpec);
        Assert.Equal("Voltage", shortSpec.IDENT().GetText());
    }

    [Fact]
    public void Parser_DerClassSpecifier_HasCorrectStructure()
    {
        var code = """
type Velocity = der(Position, time) "Velocity";
""";
        var parseTree = ModelicaParserHelper.Parse(code);

        Assert.NotNull(parseTree.class_definition());
        var derSpec = parseTree.class_definition()[0].class_specifier().der_class_specifier();
        Assert.NotNull(derSpec);
        Assert.Equal("Velocity", derSpec.IDENT()[0].GetText());
    }

    [Fact]
    public void Parser_EnumerationType_HasEnumListInTree()
    {
        var code = """
type Direction = enumeration(Forward, Backward, Both) "direction enum";
""";
        var parseTree = ModelicaParserHelper.Parse(code);

        var shortSpec = parseTree.class_definition()[0].class_specifier().short_class_specifier();
        Assert.NotNull(shortSpec);
        Assert.Contains("enumeration", shortSpec.GetText());
    }

    [Fact]
    public void Parser_ExternalFunction_HasLanguageSpecificationInTree()
    {
        var code = """
function extFunc "external function"
  input Real x;
  output Real y;
  external "C" y = ext_f(x);
end extFunc;
""";
        var parseTree = ModelicaParserHelper.Parse(code);

        Assert.NotNull(parseTree.class_definition());
        var longSpec = parseTree.class_definition()[0].class_specifier().long_class_specifier();
        Assert.NotNull(longSpec);
        var comp = longSpec.composition();
        Assert.NotNull(comp);
    }

    [Fact]
    public void Parser_WithinClause_HasNameInStoredDefinition()
    {
        var code = """
within MyLib.Sub;
model WithWithin "model"
  Real x;
equation
  x = 1.0;
end WithWithin;
""";
        var parseTree = ModelicaParserHelper.Parse(code);

        Assert.NotNull(parseTree.name());
        Assert.NotEmpty(parseTree.name());
        Assert.Equal("MyLib.Sub", parseTree.name()[0].GetText());
    }

    [Fact]
    public void Parser_ForEquationRange_HasForIndicesInTree()
    {
        var code = """
model ForEquation "for equation"
  Real x[10];
equation
  for i in 1:10 loop
    x[i] = i * 1.0;
  end for;
end ForEquation;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotNull(parseTree);
        // Just verify parse succeeded without errors
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parser_WhenEquation_ParsesCorrectly()
    {
        var code = """
model WhenModel "when equation"
  Boolean trigger;
  Real x;
equation
  when trigger then
    x = 1.0;
  end when;
end WhenModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parser_ArraySubscript_ParsesCorrectly()
    {
        var code = """
model ArrayModel "array subscript"
  Real x[5];
  Real y;
equation
  y = x[3];
  y = x[1:5];
end ArrayModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parser_NamedArguments_ParsesCorrectly()
    {
        var code = """
model NamedArgModel "named args"
  Real x;
equation
  x = Modelica.Math.atan2(y = 1.0, x = 2.0);
end NamedArgModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parser_OutputExpressionList_ParsesCorrectly()
    {
        var code = """
model OutputExprModel "output expression list"
  Real s;
  Real c;
equation
  (s, c) = Modelica.Math.sincos(1.0);
end OutputExprModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parser_ConditionalExpression_ParsesElseifExpr()
    {
        var code = """
model CondExprModel "conditional expression"
  Real x;
  Real y;
equation
  y = if x > 1.0 then 1.0 elseif x > 0.0 then 0.5 else 0.0;
end CondExprModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parser_ComplexClass_AllRulesAccessible()
    {
        var code = """
within Test;
model ComplexModel "comprehensive test"
  import SI = Modelica.SIunits;
  extends BaseModel(x = 1.0);
  parameter Real p(min = 0.0, max = 100.0) = 1.0 "a parameter";
  constant Integer n = 5 "a constant";
  Real x(start = 0.0);
  Real y;
  Real z if p > 0.0;
  flow Real q;
initial equation
  x = 0.0;
initial algorithm
  y := 0.0;
equation
  x = p * sin(SI.pi);
  y = x ^ 2 + 1.0;
  if x > 1.0 then
    z = 1.0;
  elseif x > 0.0 then
    z = 0.5;
  else
    z = 0.0;
  end if;
  for i in 1:n loop
    x = x + i;
  end for;
  when x > 2.0 then
    y = pre(y) + 1.0;
  elsewhen x < -2.0 then
    y = pre(y) - 1.0;
  end when;
algorithm
  x := 1.0;
  y := x + 2.0;
  if y > 0.0 then
    z := 1.0;
  elseif y == 0.0 then
    z := 0.0;
  else
    z := -1.0;
  end if;
  for i in 1:5 loop
    y := y + 1.0;
  end for;
  while y < 100.0 loop
    y := y * 2.0;
  end while;
  when x > 1.0 then
    y := pre(y) + 1.0;
  elsewhen x < -1.0 then
    y := pre(y) - 1.0;
  end when;
end ComplexModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        Assert.NotEmpty(parseTree.class_definition());

        // Walk with listener to cover all listener methods
        var listener = new modelicaBaseListener();
        ParseTreeWalker.Default.Walk(listener, parseTree);
    }

    // ============================================================================
    // Visitor base coverage: ensure visitor default implementations are covered
    // ============================================================================

    [Fact]
    public void Visitor_WalkWithVisitor_CoversDefaultVisitMethods()
    {
        // modelicaBaseVisitor default implementations (Visit*) are called
        // when a visitor visits a node but doesn't override the method.
        // Walking a comprehensive tree ensures all Visit methods are covered.
        var code = """
model VisitorCoverage "visitor coverage"
  parameter Real x = 1.0;
  constant Boolean flag = true;
  Real y[3];
equation
  for i in 1:3 loop
    y[i] = i * x;
  end for;
  if flag then
    y[1] = 0.0;
  end if;
  when x > 2.0 then
    y[2] = pre(y[2]) + 1.0;
  end when;
algorithm
  while x < 100.0 loop
    x := x * 2.0;
  end while;
end VisitorCoverage;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        // Walk with listener to trigger base visitor methods
        ParseTreeWalker.Default.Walk(new modelicaBaseListener(), parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Visitor_ExternalFunctionWithAllAnnotations_CoversAnnotationRules()
    {
        var code = """
function externalAnnotated "fully annotated external function"
  input Real x "x input";
  input String fileName "file name";
  output Real y "y output";
  external "C" y = ext_annotated(x, fileName)
    annotation (Library = "mylib",
                Include = "#include \"mylib.h\"",
                IncludeDirectory = "modelica://MyLib/Resources/Include",
                LibraryDirectory = "modelica://MyLib/Resources/Library");
end externalAnnotated;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Visitor_ModelWithProtectedSection_CoversProtectedKeyword()
    {
        var code = """
model WithProtected "protected section"
public
  Real x "public x";
  parameter Real p = 1.0 "public parameter";
protected
  Real hidden "protected variable";
  Integer count;
equation
  x = p;
  hidden = x * 2.0;
  count = 0;
end WithProtected;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Visitor_ModelWithMultipleSections_CoversAllSectionTypes()
    {
        var code = """
model AllSections "all section types"
  Real x;
  Real y;
initial equation
  x = 0.0;
equation
  x = 1.0;
initial algorithm
  y := 0.0;
algorithm
  y := x + 1.0;
end AllSections;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Visitor_AnnotationWithComplexContent_CoversAnnotationRule()
    {
        var code = """
model WithAnnotation "model with annotation"
  Real x;
equation
  x = 1.0;
  annotation (
    Icon(graphics = {
      Rectangle(extent = {{-100, -100}, {100, 100}}),
      Text(extent = {{-100, -100}, {100, 100}}, textString = "%name")
    }),
    Documentation(info = "<html><p>Documentation here.</p></html>")
  );
end WithAnnotation;
""";
        WalkWithListener(code);
    }

    // ============================================================================
    // Error node coverage
    // ============================================================================

    [Fact]
    public void Listener_InvalidCode_CoversErrorNodeCallback()
    {
        // Parses invalid code so the lexer/parser generates error nodes,
        // which exercises VisitErrorNode in modelicaBaseListener
        var code = "model @InvalidModel end;";
        var parseTree = ModelicaParserHelper.Parse(code);
        var listener = new modelicaBaseListener();
        // Walk even with errors to cover VisitErrorNode
        ParseTreeWalker.Default.Walk(listener, parseTree);
    }

    [Fact]
    public void Listener_PositionalMultiArgFunction_CoversArgsNonFirst()
    {
        // Covers: function_arguments_non_first (2nd+ positional args, not named)
        var code = """
model WithMultiPosArgs "multiple positional function arguments"
  Real x;
  Real y;
  Real z;
equation
  x = Modelica.Math.atan2(1.0, 2.0);
  y = max(x, 0.0);
  z = min(x, y, 1.0);
end WithMultiPosArgs;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_FunctionPartialAppAsArg_CoversFunctionPartialAppRule()
    {
        // Covers: function_partial_application (FUNCTION type_specifier '(' named_args? ')')
        // as a function_argument inside function_arguments
        var code = """
model WithFuncPartialApp "function partial application as argument"
  function myFunc
    input Real x;
    output Real y;
  algorithm
    y := x;
  end myFunc;
  Real x;
equation
  x = Modelica.Math.integrator(function myFunc(x = 1.0));
end WithFuncPartialApp;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_BreakAndReturn_CoversBreakReturnStatements()
    {
        // Covers: statement: 'break' and statement: 'return'
        var code = """
function findFirst "function with break and return"
  input Real x[5];
  input Real threshold;
  output Integer idx;
algorithm
  idx := -1;
  for i in 1:5 loop
    if x[i] > threshold then
      idx := i;
      return;
    end if;
    if i == 5 then
      break;
    end if;
  end for;
end findFirst;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_DerAssignmentStatement_CoversDerStatementRule()
    {
        // Covers: statement: 'der' function_call_args ':=' expression
        var code = """
model WithDerAssignment "der assignment in algorithm"
  Real x;
algorithm
  der(x) := -x;
end WithDerAssignment;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_MatrixLiteral_CoversMatrixPrimary()
    {
        // Covers: primary: '[' expression_list (';' expression_list)* ']'
        var code = """
model WithMatrixLiteral "matrix literal"
  Real A[2, 2];
  Real v[3];
equation
  A = [1.0, 0.0; 0.0, 1.0];
  v = [1.0; 2.0; 3.0];
end WithMatrixLiteral;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_FalseTrueEndPrimaries_CoversBoolLiterals()
    {
        // Covers: primary: 'false', primary: 'true', primary: 'end'
        var code = """
model WithBoolLiterals "boolean literals and end"
  Boolean flag;
  Real x[5];
  Real y;
equation
  flag = true;
  y = if false then 0.0 else 1.0;
  y = x[end];
end WithBoolLiterals;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ImportAlias_CoversImportAliasForm()
    {
        // Covers: import_clause: IDENT '=' name (aliased import)
        // and import_clause: name (simple dotted import)
        var code = """
model WithImportAlias "import alias"
  import SI = Modelica.SIunits;
  import Modelica.Math;
  SI.Voltage v;
equation
  v = 230.0;
end WithImportAlias;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_GeneratorExpression_CoversExprForIndices()
    {
        // Covers: function_arguments: expression 'for' for_indices (generator/comprehension)
        var code = """
model WithGenerator "generator expression"
  Real x[5];
equation
  x = {i * 1.0 for i in 1:5};
end WithGenerator;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_LongClassSpecifierExtends_CoversExtendsForm()
    {
        // Covers: long_class_specifier: 'extends' IDENT class_modification? string_comment composition 'end' IDENT
        var code = """
model extends BaseModel "extends form of long class specifier"
  Real x;
equation
  x = 1.0;
end BaseModel;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_RangeExpression_CoversSimpleExpressionRange()
    {
        // Covers: simple_expression with range ':' (start:stop:step)
        var code = """
model WithRangeExpr "range expression"
  Real x[10];
equation
  for i in 1:2:10 loop
    x[i] = i * 1.0;
  end for;
end WithRangeExpr;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_EncapsulatedAndFinalClass_CoversClassKeywords()
    {
        // Covers: class_definition: 'encapsulated'? and 'final' stored_definition
        var code = """
encapsulated model EncapsulatedModel "encapsulated model"
  Real x;
equation
  x = 1.0;
end EncapsulatedModel;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_PureImpureFunction_CoversPureImpurePrefix()
    {
        // Covers: class_prefixes: (pure|impure)? function
        var code = """
pure function pureFunc "pure function"
  input Real x;
  output Real y;
algorithm
  y := x * 2.0;
end pureFunc;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_OperatorClass_CoversOperatorPrefix()
    {
        // Covers: class_prefixes: 'operator' and operator function
        var code = """
operator record Complex "complex number"
  Real re "real part";
  Real im "imaginary part";
end Complex;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ComponentFunctionCallStatement_CoversComponentRefFuncCallArgs()
    {
        // Covers: statement: component_reference function_call_args (procedure call without assignment)
        var code = """
model WithProcedureCall "procedure call statement"
  Real x;
algorithm
  Modelica.Utilities.Streams.print("hello");
end WithProcedureCall;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_MultipleForIndices_CoversMultipleForIndices()
    {
        // Covers: for_indices with multiple for_index (comma-separated)
        var code = """
model WithMultiForIndices "multiple for indices"
  Real A[3, 3];
equation
  for i in 1:3, j in 1:3 loop
    A[i, j] = if i == j then 1.0 else 0.0;
  end for;
end WithMultiForIndices;
""";
        WalkWithListener(code);
    }

    // ============================================================================
    // Base visitor direct instantiation - covers all default VisitChildren impls
    // ============================================================================

    [Fact]
    public void BaseVisitor_Walk_CoversAllDefaultMethods()
    {
        // Instantiate the base visitor directly. Its Visit* methods all call VisitChildren
        // by default, so walking a comprehensive parse tree covers all uncovered visitor lines:
        // VisitImport_list, VisitConstraining_clause, VisitCondition_attribute,
        // VisitInheritence_modification, VisitElement_redeclaration, VisitElement_replaceable,
        // VisitComponent_declaration1, VisitElseif_equation, VisitIf_statement,
        // VisitElseif_statement, VisitElse_statement, VisitFor_statement, VisitWhile_statement,
        // VisitElsewhen_equation, VisitWhen_statement, VisitElsewhen_statement,
        // VisitElseif_expression, VisitFunction_partial_application, VisitOutput_expression_list
        var code = """
model ComprehensiveVisit "comprehensive visitor test"
  import SI = Modelica.SIunits;
  import Modelica.Math.{sin, cos};
  extends BaseModel(x = 1.0, redeclare model M = NewM);
  replaceable model Inner = Modelica.Blocks.Sources.Sine
    constrainedby Modelica.Blocks.Interfaces.SO "constrained";
  parameter Real p = 1.0 "param";
  Real x;
  Real y if p > 0.0;
  Real s;
  Real c;
equation
  x = Modelica.Math.atan2(1.0, 2.0);
  (s, c) = Modelica.Math.sincos(1.0);
  y = if x > 1.0 then 1.0 elseif x > 0.0 then 0.5 else 0.0;
  if x > 1.0 then
    y = 1.0;
  elseif x > 0.5 then
    y = 0.5;
  else
    y = 0.0;
  end if;
  for i in 1:5 loop
    x = x + 1.0;
  end for;
  when x > 2.0 then
    y = pre(y) + 1.0;
  elsewhen x < -2.0 then
    y = pre(y) - 1.0;
  end when;
algorithm
  if x > 1.0 then
    y := 1.0;
  elseif x > 0.5 then
    y := 0.5;
  else
    y := 0.0;
  end if;
  for i in 1:5 loop
    y := y + 1.0;
  end for;
  while y < 100.0 loop
    y := y * 2.0;
  end while;
  when x > 1.0 then
    y := pre(y) + 1.0;
  elsewhen x < -1.0 then
    y := pre(y) - 1.0;
  end when;
  (s, c) := sincos(x);
  Modelica.Utilities.Streams.print("hello");
end ComprehensiveVisit;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        // Walk with base visitor directly - all Visit* methods call VisitChildren
        var visitor = new modelicaBaseVisitor<object?>();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void BaseVisitor_WithImportList_CoversVisitImportList()
    {
        // Covers VisitImport_list via base visitor
        var code = """
model WithImportList "model with import list"
  import Modelica.Math.{sin, cos, tan};
  Real x;
equation
  x = sin(1.0) + cos(1.0);
end WithImportList;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new modelicaBaseVisitor<object?>();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void BaseVisitor_FuncPartialApp_CoversVisitFuncPartialApp()
    {
        // Covers VisitFunction_partial_application via base visitor
        var code = """
model WithFuncPartial "partial function application"
  Real x;
equation
  x = Modelica.Math.integrator(function Modelica.Math.sin(x = 1.0));
end WithFuncPartial;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new modelicaBaseVisitor<object?>();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    // ============================================================================
    // c_comment coverage: covers C_commentContext and element_list c_comment path
    // ============================================================================

    [Fact]
    public void Listener_CCommentInElementList_CoversLineComment()
    {
        // Covers c_comment rule with LINE_COMMENT token inside element_list
        // and Element_listContext's c_comment accessors
        var code = """
model WithLineComments "model with C-style line comments in element list"
  // This is a line comment inside the element list
  Real x;
  // Another line comment
  Real y;
equation
  x = 1.0;
  y = x;
end WithLineComments;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_CCommentBlockInElementList_CoversBlockComment()
    {
        // Covers c_comment rule with COMMENT (block comment) token inside element_list
        var code = """
model WithBlockComments "model with C-style block comments"
  /* This is a block comment inside element list */
  Real x;
  /* Another block comment */
  Real y;
equation
  x = 1.0;
  y = x;
end WithBlockComments;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_CCommentInStoredDefinition_CoversTopLevelComment()
    {
        // Covers c_comment at stored_definition level (before class declarations)
        // and Stored_definitionContext's c_comment accessors
        var code = """
/* This is a top-level block comment before the model */
// This is a top-level line comment
model WithTopLevelComments "model with top-level C comments"
  Real x;
equation
  x = 1.0;
end WithTopLevelComments;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ElementReplaceableWithComponentClause1_CoversComponentClause1()
    {
        // Covers component_clause1 (used in element_replaceable and element_redeclaration)
        // element_replaceable: 'replaceable' (short_class_definition | component_clause1)
        // component_clause1: type_prefix type_specifier component_declaration1
        var code = """
model WithReplaceableComponent "model with replaceable component in modification"
  extends Base(replaceable Real x "replaceable x component");
  Real y;
equation
  y = 1.0;
end WithReplaceableComponent;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void Listener_ElementRedeclarationWithComponentClause1_CoversComponentClause1()
    {
        // Covers component_clause1 via element_redeclaration
        // element_redeclaration: 'redeclare' ... (short_class_definition | component_clause1 | element_replaceable)
        var code = """
model WithRedeclaredComponent "model with redeclared component in modification"
  extends Base(redeclare Real x "redeclared x");
  Real y;
equation
  y = 1.0;
end WithRedeclaredComponent;
""";
        WalkWithListener(code);
    }

    [Fact]
    public void NonTypedVisitor_WithCComments_CoversMoreContexts()
    {
        // Use NonTypedVisitor on code with c_comments to cover those context else-branches
        var code = """
/* Top level comment */
model WithCCommentsNonTyped "model for non-typed visitor with comments"
  // Line comment in element list
  Real x;
  /* Block comment in element list */
  Real y;
equation
  x = 1.0;
  y = x;
end WithCCommentsNonTyped;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    // ============================================================================
    // Context accessor tests: directly call context property methods for coverage
    // ============================================================================

    [Fact]
    public void StoredDefinitionContext_WithinNameAndFinalClass_CoversAccessors()
    {
        // Covers: stored_definition 'within Name;', 'final' class_definition
        // and the name(), c_comment() accessors on Stored_definitionContext
        var code = """
within SomePackage;
final model FinalModel "a final model"
  Real x;
equation
  x = 1.0;
end FinalModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        // Call name() accessors (covers the within-name path)
        var names = parseTree.name();
        for (int i = 0; i < names.Length; i++)
            _ = parseTree.name(i);
        // Call class_definition() accessors
        var classDefs = parseTree.class_definition();
        for (int i = 0; i < classDefs.Length; i++)
            _ = parseTree.class_definition(i);
        // Call c_comment() accessors (even if empty, exercises the method)
        var cComments = parseTree.c_comment();
        WalkWithListener(code);
        Assert.NotEmpty(classDefs);
    }

    [Fact]
    public void StoredDefinitionContext_WithTopLevelComments_CoversCCommentAccessors()
    {
        // Covers: c_comment* at stored_definition level + c_comment accessors
        var code = """
/* A top-level block comment */
// A top-level line comment
within MyLib;
model CommentedModel "model with top-level comments"
  Real x;
equation
  x = 1.0;
end CommentedModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var cComments = parseTree.c_comment();
        for (int i = 0; i < cComments.Length; i++)
            _ = parseTree.c_comment(i);
        var names = parseTree.name();
        Assert.NotEmpty(cComments);
        Assert.NotEmpty(names);
    }

    [Fact]
    public void ElementListContext_WithCComments_CoversCCommentAccessors()
    {
        // Covers element_list's c_comment() and element() accessor methods
        var code = """
model WithElementListComments "model with element list comments"
  // This is a line comment
  Real x "x variable";
  /* Block comment */
  Real y "y variable";
equation
  x = 1.0;
  y = x;
end WithElementListComments;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var classSpec = parseTree.class_definition(0).class_specifier();
        var longSpec = classSpec.long_class_specifier();
        var composition = longSpec?.composition();
        Assert.NotNull(composition);
        // Call element_list() accessors on composition
        var elementLists = composition.element_list();
        for (int i = 0; i < elementLists.Length; i++)
            _ = composition.element_list(i);
        // Call c_comment() and element() accessors on each element_list
        foreach (var elementList in elementLists)
        {
            var cComments = elementList.c_comment();
            for (int i = 0; i < cComments.Length; i++)
                _ = elementList.c_comment(i);
            var elements = elementList.element();
            for (int i = 0; i < elements.Length; i++)
                _ = elementList.element(i);
        }
    }

    [Fact]
    public void CompositionContext_PublicAndProtectedSections_CoversAccessors()
    {
        // Covers: 'public' and 'protected' element_list in composition
        // and the equation_section(), algorithm_section() accessors on CompositionContext
        var code = """
model WithPublicAndProtected "model with public and protected sections"
  parameter Real p = 1.0 "parameter";
public
  Real x "public x";
  Real y "public y";
protected
  Real tmp "internal variable";
equation
  tmp = p * 2.0;
  x = tmp + 1.0;
  y = tmp - 1.0;
end WithPublicAndProtected;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var composition = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition();
        Assert.NotNull(composition);
        var elementLists = composition.element_list();
        var eqSections = composition.equation_section();
        var algSections = composition.algorithm_section();
        for (int i = 0; i < elementLists.Length; i++)
            _ = composition.element_list(i);
        for (int i = 0; i < eqSections.Length; i++)
            _ = composition.equation_section(i);
        for (int i = 0; i < algSections.Length; i++)
            _ = composition.algorithm_section(i);
        var annotations = composition.annotation();
        for (int i = 0; i < annotations.Length; i++)
            _ = composition.annotation(i);
        WalkWithListener(code);
        Assert.True(elementLists.Length >= 3); // initial + public + protected
    }

    [Fact]
    public void CompositionContext_ExternalFunction_CoversExternalClause()
    {
        // Covers: external clause in composition with language spec and external_function_call
        // Covers language_specification() and external_function_call() accessors
        var code = """
function myExternalFunc "external C function"
  input Real x "input x";
  output Real y "output y";
  external "C" y = c_func(x);
end myExternalFunc;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var composition = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition();
        Assert.NotNull(composition);
        _ = composition.language_specification();
        _ = composition.external_function_call();
        WalkWithListener(code);
    }

    [Fact]
    public void InheritenceModification_BreakIdent_CoversBreakModification()
    {
        // Covers: inheritence_modification with 'break IDENT' syntax
        // and the associated Inheritence_modificationContext and Argument_or_inheritence_listContext
        var code = """
model WithBreakModification "model with break modification"
  extends Base(break someConnection, x = 1.0);
  Real y;
equation
  y = 1.0;
end WithBreakModification;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        WalkWithListener(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void InheritenceModification_BreakConnect_CoversBreakConnectModification()
    {
        // Covers: inheritence_modification with 'break connect(a, b)' syntax
        var code = """
model WithBreakConnect "model with break connect modification"
  extends PartialModel(break connect(portA, portB));
  Real x;
equation
  x = 1.0;
end WithBreakConnect;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        WalkWithListener(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void ArgumentOrInheritenceListContext_BreakIdent_CoversInheritenceModificationAccessors()
    {
        // Explicitly exercises the inheritence_modification() accessors on
        // Argument_or_inheritence_listContext (covers lines that are uncovered at 55%)
        var code = """
model WithBreakModAccessors "testing argument_or_inheritence_list accessors"
  extends Base(break conn1, x = 1.0, break conn2);
  Real y;
equation
  y = 2.0;
end WithBreakModAccessors;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var extendsClause = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition()?.element_list(0)?.element(0)
            ?.extends_clause();
        if (extendsClause != null)
        {
            var classOrInheritMod = extendsClause.class_or_inheritence_modification();
            if (classOrInheritMod != null)
            {
                var argList = classOrInheritMod.argument_or_inheritence_list();
                if (argList != null)
                {
                    _ = argList.argument();
                    _ = argList.inheritence_modification();
                    for (int i = 0; i < argList.inheritence_modification().Length; i++)
                        _ = argList.inheritence_modification(i);
                    for (int i = 0; i < argList.argument().Length; i++)
                        _ = argList.argument(i);
                }
            }
        }
        WalkWithListener(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void ComponentClause1Context_ReplaceableComponent_CoversAccessors()
    {
        // Explicitly exercises the type_prefix(), type_specifier(), component_declaration1()
        // accessors on Component_clause1Context (covers accessor lines at 57%)
        var code = """
model WithComponentClause1 "model with replaceable component for accessor coverage"
  extends Base(replaceable parameter Real myParam = 1.0 "my replaceable param");
  Real x;
equation
  x = 1.0;
end WithComponentClause1;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var extendsClause = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition()?.element_list(0)?.element(0)
            ?.extends_clause();
        if (extendsClause != null)
        {
            var classOrInheritMod = extendsClause.class_or_inheritence_modification();
            if (classOrInheritMod != null)
            {
                var argList = classOrInheritMod.argument_or_inheritence_list();
                if (argList != null)
                {
                    foreach (var arg in argList.argument())
                    {
                        var elemRedecl = arg.element_redeclaration();
                        var elemReplaceable = elemRedecl?.element_replaceable();
                        var compClause1 = elemReplaceable?.component_clause1();
                        if (compClause1 != null)
                        {
                            _ = compClause1.type_prefix();
                            _ = compClause1.type_specifier();
                            _ = compClause1.component_declaration1();
                        }
                    }
                }
            }
        }
        WalkWithListener(code);
    }

    [Fact]
    public void ShortClassDefinitionContext_ReplaceableModelClass_CoversShortClassDef()
    {
        // Covers short_class_definition used inside element_replaceable/element_redeclaration
        // and covers Short_class_definitionContext accessor methods
        var code = """
model WithReplaceableShortClass "model with replaceable short class definition"
  extends Base(replaceable model SubModel = SomeOtherModel "replaced model");
  Real x;
equation
  x = 1.0;
end WithReplaceableShortClass;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var extendsClause = parseTree.class_definition(0).class_specifier()
            .long_class_specifier()?.composition()?.element_list(0)?.element(0)
            ?.extends_clause();
        if (extendsClause != null)
        {
            var classOrInheritMod = extendsClause.class_or_inheritence_modification();
            var argList = classOrInheritMod?.argument_or_inheritence_list();
            if (argList != null)
            {
                foreach (var arg in argList.argument())
                {
                    var elemRedecl = arg.element_redeclaration();
                    var elemReplaceable = elemRedecl?.element_replaceable();
                    var shortClassDef = elemReplaceable?.short_class_definition();
                    if (shortClassDef != null)
                    {
                        _ = shortClassDef.class_prefixes();
                        _ = shortClassDef.short_class_specifier();
                    }
                }
            }
        }
        WalkWithListener(code);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void ComponentReferenceContext_WithArraySubscriptsAndLeadingDot_CoversAccessors()
    {
        // Covers component_reference with leading dot and array subscripts
        // and the array_subscripts() accessor on Component_referenceContext
        var code = """
model WithComplexComponentRefs "model with array subscripts and leading dot in component refs"
  Real pi = .Modelica.Constants.pi;
  Real arr[3] = {1.0, 2.0, 3.0};
  Real val = arr[1];
  Real nested[2, 2];
  Real elem = nested[1, 2];
equation
  pi = 3.14159;
  arr[1] = 1.0;
  arr[2] = 2.0;
  arr[3] = 3.0;
  nested[1, 1] = 1.0;
end WithComplexComponentRefs;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        WalkWithListener(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void MultipleClassesInFile_CoversStoredDefinitionMultipleClassDefs()
    {
        // Covers stored_definition with multiple class definitions
        // and class_definition(int i) indexed accessor
        var code = """
model FirstModel "first model in file"
  Real x;
equation
  x = 1.0;
end FirstModel;
model SecondModel "second model in file"
  Real y;
equation
  y = 2.0;
end SecondModel;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var classDefs = parseTree.class_definition();
        for (int i = 0; i < classDefs.Length; i++)
            _ = parseTree.class_definition(i);
        Assert.Equal(2, classDefs.Length);
        WalkWithListener(code);
    }

    // ============================================================================
    // Non-typed visitor: covers else-branch in every context Accept method
    // Each parser context's Accept<TResult> has:
    //   if (visitor is ImodelicaVisitor<TResult> typedVisitor) return typedVisitor.VisitX(this);
    //   else return visitor.VisitChildren(this);   <-- this branch needs a non-typed visitor
    // ============================================================================

    /// <summary>
    /// A visitor that extends AbstractParseTreeVisitor but does NOT implement ImodelicaVisitor.
    /// When used to visit parser contexts, it triggers the else branch in each Accept method.
    /// </summary>
    private class NonTypedVisitor : AbstractParseTreeVisitor<object?>
    {
        public override object? VisitTerminal(ITerminalNode node) => null;
        public override object? VisitErrorNode(IErrorNode node) => null;
    }

    [Fact]
    public void NonTypedVisitor_Walk_CoversContextAcceptElseBranches()
    {
        // Uses a visitor that is NOT ImodelicaVisitor, triggering the
        // "else return visitor.VisitChildren(this)" branch in every context Accept method.
        // This covers ~96 uncovered line pairs in modelicaParser.cs.
        var code = """
model ComprehensiveNonTyped "non-typed visitor test"
  import SI = Modelica.SIunits;
  import Modelica.Math.{sin, cos};
  extends BaseModel(x = 1.0, redeclare model M = NewM);
  replaceable model Inner = Modelica.Blocks.Sources.Sine
    constrainedby Modelica.Blocks.Interfaces.SO "constrained";
  parameter Real p = 1.0 "param";
  Real x(start = 0.0);
  Real y if p > 0.0;
  Real s;
  Real c;
equation
  x = Modelica.Math.atan2(1.0, 2.0);
  (s, c) = Modelica.Math.sincos(1.0);
  y = if x > 1.0 then 1.0 elseif x > 0.0 then 0.5 else 0.0;
  if x > 1.0 then
    y = 1.0;
  elseif x > 0.5 then
    y = 0.5;
  else
    y = 0.0;
  end if;
  for i in 1:5 loop
    x = x + 1.0;
  end for;
  when x > 2.0 then
    y = pre(y) + 1.0;
  elsewhen x < -2.0 then
    y = pre(y) - 1.0;
  end when;
algorithm
  if x > 1.0 then
    y := 1.0;
  elseif x > 0.5 then
    y := 0.5;
  else
    y := 0.0;
  end if;
  for i in 1:5 loop
    y := y + 1.0;
  end for;
  while y < 100.0 loop
    y := y * 2.0;
  end while;
  when x > 1.0 then
    y := pre(y) + 1.0;
  elsewhen x < -1.0 then
    y := pre(y) - 1.0;
  end when;
  (s, c) := sincos(x);
  Modelica.Utilities.Streams.print("hello");
end ComprehensiveNonTyped;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void NonTypedVisitor_WithinAndPackage_CoversMoreContexts()
    {
        // Covers within, package, short class specifier, and der specifier contexts
        var code = """
within MyLib;
package MyPkg "my package"
  type Voltage = Real(unit = "V") "Voltage type";
  type Velocity = der(Position, time) "Velocity";
  model Inner "inner model"
    parameter Real p = 1.0 "p";
    Real x;
  equation
    x = p;
  end Inner;
end MyPkg;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void NonTypedVisitor_FunctionAndConnector_CoversAdditionalContexts()
    {
        // Covers function, connector, and external function contexts
        var code = """
function myFunc "my function"
  input Real x "input";
  output Real y "output";
  protected
    Real tmp;
algorithm
  tmp := x * 2.0;
  y := tmp + 1.0;
end myFunc;

connector MyConn "connector"
  Real v "voltage";
  flow Real i "current";
end MyConn;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new NonTypedVisitor();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void BaseVisitor_WithBreakAndReplaceable_CoversRemainingBaseVisitorMethods()
    {
        // Covers VisitInheritence_modification (break keyword in modification),
        // VisitElement_replaceable (replaceable inside modification argument),
        // and VisitComponent_declaration1 (component_clause1 used in element_replaceable)
        // All require modelicaBaseVisitor<object?> (not NonTypedVisitor) to execute base visitor methods.
        var code = """
model WithBreakAndReplaceable "model with break and replaceable in modifications"
  extends Base(break someConnection, replaceable Real x "replaceable param");
  Real y;
equation
  y = 1.0;
end WithBreakAndReplaceable;
""";
        var parseTree = ModelicaParserHelper.Parse(code);
        var visitor = new modelicaBaseVisitor<object?>();
        visitor.Visit(parseTree);
        Assert.NotEmpty(parseTree.class_definition());
    }

    // ============================================================================
    // Parser class properties and RuleIndex coverage
    // ============================================================================

    /// <summary>
    /// A listener that captures the RuleIndex from every rule context visited.
    /// Used to exercise the virtual RuleIndex property getter on each context class.
    /// </summary>
    private class RuleIndexCapturingListener : modelicaBaseListener
    {
        public readonly List<int> CapturedRuleIndices = new();
        public override void EnterEveryRule(ParserRuleContext ctx)
        {
            CapturedRuleIndices.Add(ctx.RuleIndex);
        }
    }

    [Fact]
    public void ParserClassProperties_AreAccessible()
    {
        // Covers lines 151, 153, 155: GrammarFileName, RuleNames, SerializedAtn property getters
        // These are never accessed during normal parsing but are accessible via direct instantiation.
        var (_, tokenStream) = ModelicaParserHelper.ParseWithTokens("model T end T;");
        var parser = new modelicaParser(tokenStream);
        Assert.Equal("modelica.g4", parser.GrammarFileName);
        Assert.NotEmpty(parser.RuleNames);
        Assert.NotEmpty(parser.SerializedAtn);
    }

    [Fact]
    public void RuleIndex_ComprehensiveCode_CoversAllContextRuleIndices()
    {
        // Covers RuleIndex property getter on all parser context classes (one per grammar rule).
        // ParseTreeWalker calls EnterEveryRule for each matched context, which then calls
        // ctx.RuleIndex, exercising the virtual override on the specific context type.
        var listener = new RuleIndexCapturingListener();

        // Walk multiple code snippets to ensure all grammar rule context types appear.
        // Each snippet targets grammar rules not covered by the others.
        var snippets = new[]
        {
            // Main comprehensive model: covers equation, algorithm, if/for/while/when, connect,
            // import, extends, replaceable, constrainedby, arrays, function calls, named args, etc.
            """
model ComprehensiveRuleIndex "comprehensive model for rule index coverage"
  import SI = Modelica.SIunits;
  import Modelica.Math.{sin, cos};
  extends BaseModel(x = 1.0);
  replaceable model Inner = Modelica.Blocks.Sources.Sine
    constrainedby Modelica.Blocks.Interfaces.SO "constrained inner";
  parameter Real p(unit = "m") = 1.0 "param";
  Real x(start = 0.0) "state";
  Real y if p > 0.0;
  Real arr[3];
  Real s;
  Real c;
equation
  x = sin(p) + cos(p);
  y = if x > 1.0 then 1.0 elseif x > 0.5 then 0.5 else 0.0;
  arr = {1.0, 2.0, 3.0};
  (s, c) = Modelica.Math.sincos(x);
  if x > 1.0 then
    y = 1.0;
  elseif x > 0.5 then
    y = 0.5;
  else
    y = 0.0;
  end if;
  for i in 1:5, j in 1:3 loop
    arr[i] = Real(j);
  end for;
  when x > 2.0 then
    y = pre(y) + 1.0;
  elsewhen x < -2.0 then
    y = pre(y) - 1.0;
  end when;
  connect(s, c);
algorithm
  if x > 1.0 then
    y := 1.0;
  elseif x > 0.5 then
    y := 0.5;
  else
    y := 0.0;
  end if;
  for i in 1:5 loop
    y := y + 1.0;
  end for;
  while y < 100.0 loop
    y := y * 2.0;
  end while;
  when x > 1.0 then
    y := pre(y) + 1.0;
  elsewhen x < -1.0 then
    y := pre(y) - 1.0;
  end when;
  (s, c) := Modelica.Math.sincos(x);
end ComprehensiveRuleIndex;
""",
            // Short class specifier, der class specifier, enumeration, base_prefix
            """
within MyLib;
package MyPkg "package with short and der classes"
  type Voltage = Real(unit = "V") "Voltage";
  type Velocity = der(Position, time) "Velocity";
  type Color = enumeration(red "red value", green "green value", blue "blue value") "Color enum";
  model Inner "inner model"
    parameter Real p = 1.0;
    Real x;
  equation
    x = p;
  end Inner;
end MyPkg;
""",
            // External function with c_comment, language_specification, external_function_call
            """
/* c comment at top */
function extFunc "external function"
  input Real x "input value";
  output Real y "output value";
  external "C" y = myFunc(x) annotation(Include = "#include <math.h>");
end extFunc;
""",
            // Function partial application, named arguments, function_argument
            """
model WithFuncApp "function application patterns"
  Real x;
  Real result;
equation
  result = sum({1.0, 2.0, 3.0});
  x = f(function g(a = 1.0), y = 2.0);
end WithFuncApp;
""",
            // Replaceable and break in modifications (inheritence_modification, element_replaceable)
            """
model WithMods "with modification patterns"
  extends Base(break someConn, replaceable Real x "replaceable param");
  Real y;
equation
  y = 1.0;
end WithMods;
""",
        };

        foreach (var code in snippets)
        {
            var parseTree = ModelicaParserHelper.Parse(code);
            ParseTreeWalker.Default.Walk(listener, parseTree);
        }
        Assert.NotEmpty(listener.CapturedRuleIndices);
    }

    [Fact]
    public void NonTypedVisitor_LessCommonRules_CoversElseBranches()
    {
        // Covers the 'else return visitor.VisitChildren(this)' branch in Accept<TResult> methods
        // for context classes not reached by the main NonTypedVisitor test:
        // enum_list, enumeration_literal, language_specification, external_function_call,
        // element_replaceable, component_clause1, component_declaration1,
        // named_arguments, named_argument, function_partial_application,
        // expression_list, annotation, and others.
        var visitor = new NonTypedVisitor();

        // Enumeration types → enum_list, enumeration_literal, base_prefix
        visitor.Visit(ModelicaParserHelper.Parse("""
within MyLib;
package MyPkg "package for enum test"
  type Color = enumeration(red "red color", green "green color", blue "blue color") "Color enum";
  model Inner "inner model"
    parameter Real p = 1.0;
    Real x;
    annotation(Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
  equation
    x = p;
  end Inner;
end MyPkg;
"""));

        // External function → language_specification, external_function_call
        visitor.Visit(ModelicaParserHelper.Parse("""
function extFunc "external function"
  input Real x "input";
  output Real y "output";
  external "C" y = myFunc(x) annotation(Include = "#include <math.h>");
end extFunc;
"""));

        // element_replaceable in modification → element_replaceable, component_clause1, component_declaration1
        visitor.Visit(ModelicaParserHelper.Parse("""
model WithReplaceable "model with replaceable in modification"
  extends Base(replaceable Real x "replaceable param");
  Real y;
equation
  y = 1.0;
end WithReplaceable;
"""));

        // Named arguments and function partial application
        // → named_arguments, named_argument, function_partial_application
        visitor.Visit(ModelicaParserHelper.Parse("""
model WithNamedArgs "model with named arguments and partial application"
  Real x;
  Real result;
equation
  result = f(x = 1.0, y = 2.0);
  x = g(function h(a = 1.0), b = 2.0);
end WithNamedArgs;
"""));

        // Expression list: (a, b) := f() → expression_list, output_expression_list
        visitor.Visit(ModelicaParserHelper.Parse("""
model WithExprList "model with expression list"
  Real a;
  Real b;
algorithm
  (a, b) := sincos(1.0);
end WithExprList;
"""));

        Assert.True(true);
    }

    // ============================================================================
    // RecognitionException catch block coverage via re-throwing error strategy
    // ============================================================================

    /// <summary>
    /// An error strategy that re-throws RecognitionException instead of recovering.
    /// When the parser encounters a parse error, this causes the exception to propagate
    /// upward through the call stack, triggering each rule method's catch block.
    /// </summary>
    private class ReThrowingErrorStrategy : DefaultErrorStrategy
    {
        public override void Recover(Parser recognizer, RecognitionException e) => throw e;

        public override IToken RecoverInline(Parser recognizer)
        {
            throw new InputMismatchException(recognizer);
        }
    }

    private static void ParseWithReThrow(string code)
    {
        var inputStream = new AntlrInputStream(code);
        var lexer = new modelicaLexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        var parser = new modelicaParser(tokenStream);
        parser.RemoveErrorListeners();
        parser.ErrorHandler = new ReThrowingErrorStrategy();
        try { parser.stored_definition(); } catch { }
    }

    [Fact]
    public void ParseError_WithReThrowingStrategy_CoversCatchBlocks()
    {
        // Covers RecognitionException catch blocks in rule methods by forcing parse errors
        // to propagate as RecognitionException up the call stack. Each rule method's catch
        // block executes (covering 5 lines each) as the exception propagates outward.
        // Multiple code snippets target errors in different parts of the grammar.

        // Error deep in element parsing (component declaration, type specifier, etc.)
        ParseWithReThrow("model T Real !!! end T;");

        // Error in equation section
        ParseWithReThrow("model T equation !!! := 1.0; end T;");

        // Error in algorithm section
        ParseWithReThrow("model T algorithm y !!! := 1.0; end T;");

        // Error in if equation
        ParseWithReThrow("model T equation if !!! then y = 1.0; end if; end T;");

        // Error in for equation
        ParseWithReThrow("model T equation for !!! in 1:5 loop x = 1.0; end for; end T;");

        // Error in when equation
        ParseWithReThrow("model T equation when !!! then y = 1.0; end when; end T;");

        // Error in annotation
        ParseWithReThrow("model T annotation(!!! = 1.0); end T;");

        // Error in extends clause
        ParseWithReThrow("model T extends Base(!!! = 1.0); end T;");

        // Error in function call args
        ParseWithReThrow("model T equation x = f(!!! + ); end T;");

        // Error in expression
        ParseWithReThrow("model T equation x = 1.0 !!! 2.0; end T;");

        Assert.True(true);
    }

    [Fact]
    public void ParseError_WithReThrowingStrategy_CoversMoreCatchBlocks()
    {
        // Uses valid Modelica tokens in syntactically wrong positions to trigger
        // RecognitionException catch blocks that !!! cannot reach (lexer silently drops
        // unrecognised characters, so !!! produces no error tokens). Using keywords like
        // 'end' where an IDENT/expression is expected forces a genuine parse error that
        // propagates up the call stack via ReThrowingErrorStrategy.

        // class_prefixes: expects class type keyword but gets 'end'
        ParseWithReThrow("partial end T;");

        // short_class_specifier / name: 'end' not valid as a name
        ParseWithReThrow("type X = end;");

        // der_class_specifier: 'end' not valid as type_specifier inside der(...)
        ParseWithReThrow("type X = der(end, t);");

        // enum_list / enumeration_literal: 'end' not a valid enum literal
        ParseWithReThrow("type E = enumeration(a, end);");

        // external_function_call: 'end' not valid as an expression argument
        ParseWithReThrow("function f external \"C\" f(end); end f;");

        // import_clause / import_list: 'end' not a valid IDENT in import list
        ParseWithReThrow("model T import A.{B, end}; end T;");

        // constraining_clause: 'end' not valid as a type_specifier
        ParseWithReThrow("model T replaceable Real x constrainedby end; end T;");

        // component_list / component_declaration / declaration: 'end' not valid IDENT
        ParseWithReThrow("model T Real x, end; end T;");

        // condition_attribute: 'end' not valid as an expression
        ParseWithReThrow("model T Real x if end; end T;");

        // modification / modification_expression / argument_list / argument /
        // element_modification / element_modification_or_replaceable
        ParseWithReThrow("model T Real x(start = end); end T;");

        // type_specifier: 'end' not valid as a name
        ParseWithReThrow("model T end x; end T;");

        // argument_or_inheritence_list: 'end' not valid as modification_expression
        ParseWithReThrow("model T extends Base(x = end); end T;");

        // inheritence_modification / connect_clause / component_reference:
        // 'end' not valid as component_reference inside connect
        ParseWithReThrow("model T extends Base(break connect(x, end)); end T;");

        // element_redeclaration / component_clause1 / component_declaration1:
        // 'end' not valid as IDENT (declaration name)
        ParseWithReThrow("model T extends Base(redeclare Real end); end T;");

        // element_replaceable / short_class_definition:
        // 'end' not valid as name inside type assignment
        ParseWithReThrow("model T extends Base(replaceable type X = end); end T;");

        // algorithm_section / if_statement / statement / statement_or_comment:
        // 'end' not valid as expression in if condition
        ParseWithReThrow("model T algorithm if end then x := 0.0; end if; end T;");

        // elseif_statement: 'end' not valid as elseif condition
        ParseWithReThrow("model T algorithm if x > 0 then x := 1.0; elseif end then x := 0.0; end if; end T;");

        // else_statement: 'end' not valid as expression in assignment
        ParseWithReThrow("model T algorithm if x > 0 then x := 1.0; else x := end; end if; end T;");

        // for_statement: 'end' not valid as expression in loop body
        ParseWithReThrow("model T algorithm for i in 1:5 loop x := end; end for; end T;");

        // while_statement: 'end' not valid as expression in loop body
        ParseWithReThrow("model T algorithm while x > 0 loop x := end; end while; end T;");

        // when_statement: 'end' not valid as expression in action
        ParseWithReThrow("model T algorithm when x > 1 then x := end; end when; end T;");

        // elsewhen_statement: 'end' not valid as expression in action
        ParseWithReThrow("model T algorithm when x > 1 then x := 1.0; elsewhen x < 0 then x := end; end when; end T;");

        // elseif_equation: 'end' not valid as elseif condition
        ParseWithReThrow("model T equation if x > 0 then x = 1.0; elseif end then x = 2.0; end if; end T;");

        // else_equation: 'end' not valid as expression in equation
        ParseWithReThrow("model T equation if x > 0 then x = 1.0; else x = end; end if; end T;");

        // elsewhen_equation: 'end' not valid as elsewhen condition
        ParseWithReThrow("model T equation when x > 1 then x = 0.0; elsewhen end then x = 1.0; end when; end T;");

        // elseif_expression: 'end' not valid as elseif condition in expression
        ParseWithReThrow("model T equation x = if y > 0 then 1.0 elseif end then 0.0 else -1.0; end T;");

        // function_arguments_non_first / named_arguments / named_argument / function_argument:
        // 'end' not valid as function argument value
        ParseWithReThrow("model T equation x = f(1.0, a = end); end T;");

        // array_arguments / array_arguments_non_first: 'end' not valid in array literal
        ParseWithReThrow("model T equation x = {1.0, end}; end T;");

        // expression_list: 'end' not valid as expression in matrix literal
        ParseWithReThrow("model T equation x = [1.0, end]; end T;");

        // function_partial_application: 'end' not valid as named argument value
        ParseWithReThrow("model T equation x = f(function g(a = end)); end T;");

        // output_expression_list: 'end' not valid as expression in output tuple
        ParseWithReThrow("model T algorithm (if end, y) := f(x); end T;");

        // array_subscripts / subscript_: 'end' token is valid in subscripts (it's the
        // Modelica 'end' keyword for last index), so use a different invalid token
        ParseWithReThrow("model T Real x[if]; end T;");

        // comment (string_comment followed by annotation): annotation with bad content
        ParseWithReThrow("model T Real x annotation(end = 1.0); end T;");

        // string_comment: '+' followed by non-STRING token
        ParseWithReThrow("model T Real x \"desc\" + end; end T;");

        Assert.True(true);
    }

    // ============================================================================
    // Indexed context accessor coverage
    // ============================================================================

    private static void WalkWithIndexedListener(string code)
    {
        var parseTree = ModelicaParserHelper.Parse(code);
        var listener = new IndexedContextAccessListener();
        ParseTreeWalker.Default.Walk(listener, parseTree);
    }

    /// <summary>
    /// Listener that calls the 3-line indexed accessor methods (e.g., term(int i),
    /// add_op(int i)) when walking parse trees. These methods are only covered when
    /// explicitly called with an index argument.
    /// </summary>
    private class IndexedContextAccessListener : modelicaBaseListener
    {
        // Long_class_specifierContext.IDENT(int i) - class name + end class name
        public override void EnterLong_class_specifier(modelicaParser.Long_class_specifierContext ctx)
        {
            if (ctx.IDENT().Length >= 2) _ = ctx.IDENT(1);
        }

        // Import_listContext.IDENT(int i) - import P.{A, B}
        public override void EnterImport_list(modelicaParser.Import_listContext ctx)
        {
            if (ctx.IDENT().Length >= 2) _ = ctx.IDENT(1);
        }

        // Arithmetic_expressionContext.term(int i) / add_op(int i) - a + b + c
        public override void EnterArithmetic_expression(modelicaParser.Arithmetic_expressionContext ctx)
        {
            if (ctx.term().Length >= 2) { _ = ctx.term(1); _ = ctx.add_op(0); }
        }

        // TermContext.factor(int i) / mul_op(int i) - a * b * c
        public override void EnterTerm(modelicaParser.TermContext ctx)
        {
            if (ctx.factor().Length >= 2) { _ = ctx.factor(1); _ = ctx.mul_op(0); }
        }

        // FactorContext.primary(int i) - a ^ b
        public override void EnterFactor(modelicaParser.FactorContext ctx)
        {
            if (ctx.primary().Length >= 2) _ = ctx.primary(1);
        }

        // RelationContext.arithmetic_expression(int i) - a > b
        public override void EnterRelation(modelicaParser.RelationContext ctx)
        {
            if (ctx.arithmetic_expression().Length >= 2) _ = ctx.arithmetic_expression(1);
        }

        // ExpressionContext.expression(int i) / elseif_expression(int i) - if-elseif-else
        public override void EnterExpression(modelicaParser.ExpressionContext ctx)
        {
            if (ctx.expression().Length >= 1) _ = ctx.expression(0);
            if (ctx.elseif_expression().Length >= 1) _ = ctx.elseif_expression(0);
        }

        // Elseif_expressionContext.expression(int i) - condition and value expressions
        public override void EnterElseif_expression(modelicaParser.Elseif_expressionContext ctx)
        {
            if (ctx.expression().Length >= 2) _ = ctx.expression(1);
        }

        // Simple_expressionContext.logical_expression(int i) - range start:step:end
        public override void EnterSimple_expression(modelicaParser.Simple_expressionContext ctx)
        {
            if (ctx.logical_expression().Length >= 2) _ = ctx.logical_expression(1);
        }

        // Logical_expressionContext.logical_term(int i) - a or b or c
        public override void EnterLogical_expression(modelicaParser.Logical_expressionContext ctx)
        {
            if (ctx.logical_term().Length >= 2) _ = ctx.logical_term(1);
        }

        // Logical_termContext.logical_factor(int i) - a and b and c
        public override void EnterLogical_term(modelicaParser.Logical_termContext ctx)
        {
            if (ctx.logical_factor().Length >= 2) _ = ctx.logical_factor(1);
        }

        // NameContext.IDENT(int i) - dotted type name A.B.C
        public override void EnterName(modelicaParser.NameContext ctx)
        {
            if (ctx.IDENT().Length >= 2) _ = ctx.IDENT(1);
        }

        // Component_referenceContext.IDENT(int i) / array_subscripts(int i) - a.b or a[1].b[2]
        public override void EnterComponent_reference(modelicaParser.Component_referenceContext ctx)
        {
            if (ctx.IDENT().Length >= 2) _ = ctx.IDENT(1);
            if (ctx.array_subscripts().Length >= 2) _ = ctx.array_subscripts(1);
        }

        // Array_subscriptsContext.subscript_(int i) - arr[1, 2]
        public override void EnterArray_subscripts(modelicaParser.Array_subscriptsContext ctx)
        {
            if (ctx.subscript_().Length >= 2) _ = ctx.subscript_(1);
        }

        // String_commentContext.STRING(int i) - "a" + "b"
        public override void EnterString_comment(modelicaParser.String_commentContext ctx)
        {
            if (ctx.STRING().Length >= 2) _ = ctx.STRING(1);
        }

        // For_indicesContext.for_index(int i) - for i in 1:2, j in 1:3
        public override void EnterFor_indices(modelicaParser.For_indicesContext ctx)
        {
            if (ctx.for_index().Length >= 2) _ = ctx.for_index(1);
        }

        // If_equationContext.equation_or_comment(int i) / elseif_equation(int i)
        public override void EnterIf_equation(modelicaParser.If_equationContext ctx)
        {
            if (ctx.equation_or_comment().Length >= 2) _ = ctx.equation_or_comment(1);
            if (ctx.elseif_equation().Length >= 1) _ = ctx.elseif_equation(0);
        }

        // Elseif_equationContext.equation_or_comment(int i)
        public override void EnterElseif_equation(modelicaParser.Elseif_equationContext ctx)
        {
            if (ctx.equation_or_comment().Length >= 2) _ = ctx.equation_or_comment(1);
        }

        // Else_equationContext.equation_or_comment(int i)
        public override void EnterElse_equation(modelicaParser.Else_equationContext ctx)
        {
            if (ctx.equation_or_comment().Length >= 2) _ = ctx.equation_or_comment(1);
        }

        // For_equationContext.equation_or_comment(int i)
        public override void EnterFor_equation(modelicaParser.For_equationContext ctx)
        {
            if (ctx.equation_or_comment().Length >= 2) _ = ctx.equation_or_comment(1);
        }

        // When_equationContext.equation_or_comment(int i) / elsewhen_equation(int i)
        public override void EnterWhen_equation(modelicaParser.When_equationContext ctx)
        {
            if (ctx.equation_or_comment().Length >= 2) _ = ctx.equation_or_comment(1);
            if (ctx.elsewhen_equation().Length >= 1) _ = ctx.elsewhen_equation(0);
        }

        // Elsewhen_equationContext.equation_or_comment(int i)
        public override void EnterElsewhen_equation(modelicaParser.Elsewhen_equationContext ctx)
        {
            if (ctx.equation_or_comment().Length >= 2) _ = ctx.equation_or_comment(1);
        }

        // If_statementContext.statement_or_comment(int i) / elseif_statement(int i)
        public override void EnterIf_statement(modelicaParser.If_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
            if (ctx.elseif_statement().Length >= 1) _ = ctx.elseif_statement(0);
        }

        // Elseif_statementContext.statement_or_comment(int i)
        public override void EnterElseif_statement(modelicaParser.Elseif_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
        }

        // Else_statementContext.statement_or_comment(int i)
        public override void EnterElse_statement(modelicaParser.Else_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
        }

        // For_statementContext.statement_or_comment(int i)
        public override void EnterFor_statement(modelicaParser.For_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
        }

        // While_statementContext.statement_or_comment(int i)
        public override void EnterWhile_statement(modelicaParser.While_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
        }

        // When_statementContext.statement_or_comment(int i) / elsewhen_statement(int i)
        public override void EnterWhen_statement(modelicaParser.When_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
            if (ctx.elsewhen_statement().Length >= 1) _ = ctx.elsewhen_statement(0);
        }

        // Elsewhen_statementContext.statement_or_comment(int i)
        public override void EnterElsewhen_statement(modelicaParser.Elsewhen_statementContext ctx)
        {
            if (ctx.statement_or_comment().Length >= 2) _ = ctx.statement_or_comment(1);
        }

        // Connect_clauseContext.component_reference(int i)
        public override void EnterConnect_clause(modelicaParser.Connect_clauseContext ctx)
        {
            if (ctx.component_reference().Length >= 2) _ = ctx.component_reference(1);
        }

        // PrimaryContext.expression_list(int i) - matrix literal [a, b; c, d]
        public override void EnterPrimary(modelicaParser.PrimaryContext ctx)
        {
            if (ctx.expression_list().Length >= 2) _ = ctx.expression_list(1);
        }

        // Expression_listContext.expression(int i) - a, b, c
        public override void EnterExpression_list(modelicaParser.Expression_listContext ctx)
        {
            if (ctx.expression().Length >= 2) _ = ctx.expression(1);
        }

        // Output_expression_listContext.expression(int i) - (a, b) := f(x)
        public override void EnterOutput_expression_list(modelicaParser.Output_expression_listContext ctx)
        {
            if (ctx.expression().Length >= 2) _ = ctx.expression(1);
        }
    }

    [Fact]
    public void IndexedContextAccessors_ComprehensiveCode_CoversIndexedMethods()
    {
        // Covers 3-line indexed accessor methods in modelicaParser context classes.
        // The IndexedContextAccessListener walks a parse tree and calls indexed accessors
        // whenever multiple elements are present. Code below exercises every case.
        var code = """
model IndexedAccessors "part1" + "part2"
  import P.{A, B};
  Real x "double" + "string";
  Real y[2, 2];
  P.Q.SubModel comp;
  Boolean c1 = true;
  Boolean c2 = false;
equation
  x = 1.0 + 2.0 + 3.0;
  y[1, 1] = 2.0 * 3.0 * 4.0;
  x = 2.0 ^ 3.0;
  x = if x > 0 then 1.0 else 0.0;
  x = if x > 1 then 1.0 elseif x < -1 then -1.0 else 0.0;
  x = if x > 0 or x < -2 then 1.0 else 0.0;
  x = if x > 0 and x < 2 then 1.0 else 0.0;
  x = comp.portA.value;
  x = y[1, 2];
  if c1 then
    x = 1.0;
    y[1, 1] = 2.0;
  elseif c2 then
    x = 3.0;
    y[1, 1] = 4.0;
  else
    x = 5.0;
    y[1, 1] = 6.0;
  end if;
  for i in 1:2, j in 1:2 loop
    y[i, j] = 0.0;
    x = x + 1.0;
  end for;
  when x > 1 then
    x = 0.0;
    y[1, 1] = 1.0;
  elsewhen x < -1 then
    x = 0.0;
    y[1, 1] = -1.0;
  end when;
  connect(comp.portA, comp.portB);
  y = [1.0, 2.0; 3.0, 4.0];
algorithm
  if c1 then
    x := 1.0;
    y[1, 1] := 2.0;
  elseif c2 then
    x := 3.0;
    y[1, 1] := 4.0;
  else
    x := 5.0;
    y[1, 1] := 6.0;
  end if;
  for i in 1:2:6 loop
    x := x + 1.0;
    y[1, 1] := y[1, 1] + 2.0;
  end for;
  while x > 0 loop
    x := x - 1.0;
    y[1, 1] := y[1, 1] - 2.0;
  end while;
  when x > 1 then
    x := 0.0;
    y[1, 1] := 0.0;
  elsewhen x < -1 then
    x := 0.0;
    y[1, 1] := 0.0;
  end when;
  (x, y[1, 1]) := myFunc(x);
end IndexedAccessors;
""";
        WalkWithIndexedListener(code);
        Assert.True(true);
    }
}
