namespace ModelicaParser.Tests.ModelicaRendererTests;

/// <summary>
/// Tests for ModelicaRenderer that verify der_class_specifier formatting.
/// Grammar rule: IDENT '=' 'der' '(' type_specifier ',' IDENT (',' IDENT)* ')' comment
/// </summary>
public class DerClassSpecifierTests
{
  [Fact]
  public void DerClassSpecifier_FormatsCorrectly()
  {
    var testModel = """
        type TransferFunction = der(Laplace, s, s);
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void DerClassSpecifierSingleVariable_FormatsCorrectly()
  {
    var testModel = """
        type Derivative = der(BaseType, x);
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void DerClassSpecifierThreeVariables_FormatsCorrectly()
  {
    var testModel = """
        type ThirdOrder = der(BaseFunc, s, s, s);
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void DerClassSpecifierWithDescription_FormatsCorrectly()
  {
    var testModel = """
        type TransferFunction = der(Laplace, s) "A transfer function";
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void DerClassSpecifierWithAnnotation_FormatsCorrectly()
  {
    var testModel = """
        type TransferFunction = der(Laplace, s) "A transfer function"
          annotation (Evaluate=true);
        """;
    TestHelpers.AssertClass(testModel);
  }

  [Fact]
  public void DerClassSpecifierQualifiedType_FormatsCorrectly()
  {
    var testModel = """
        type TF = der(Modelica.Blocks.Types.Laplace, s);
        """;
    TestHelpers.AssertClass(testModel);
  }
}
