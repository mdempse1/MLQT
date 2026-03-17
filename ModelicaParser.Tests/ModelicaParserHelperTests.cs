using Xunit;
using ModelicaParser.Helpers;

namespace ModelicaParser.Tests;

public class ModelicaParserHelperTests
{
    [Fact]
    public void Parse_SimpleModel_ReturnsParseTree()
    {
        // Arrange
        var code = @"
model SimpleModel
  Real x;
end SimpleModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
        Assert.NotEmpty(parseTree.class_definition());
    }

    [Fact]
    public void Parse_EmptyCode_ReturnsEmptyParseTree()
    {
        // Arrange
        var code = "";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
    }

    [Fact]
    public void Parse_WithinStatement_ParsesCorrectly()
    {
        // Arrange
        var code = @"
within MyPackage;
model TestModel
  Real x;
end TestModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.name());
        Assert.NotEmpty(parseTree.name());
    }

    [Fact]
    public void Parse_ComplexModel_ParsesWithoutErrors()
    {
        // Arrange
        var code = @"
model ComplexModel
  parameter Real p = 1.0;
  Real x(start=0);
  Real y;
equation
  der(x) = -p * x;
  y = x * 2;
end ComplexModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void Parse_MultipleModels_ParsesAll()
    {
        // Arrange
        var code = @"
model Model1
  Real x;
end Model1;

model Model2
  Real y;
end Model2;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Equal(2, parseTree.class_definition().Length);
    }

    [Fact]
    public void Parse_WithComments_ParsesCorrectly()
    {
        // Arrange
        var code = @"
// This is a comment
model CommentedModel
  Real x; // Variable x
  /* Block comment */
  Real y;
end CommentedModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void Parse_Function_ParsesCorrectly()
    {
        // Arrange
        var code = @"
function myFunction
  input Real x;
  output Real y;
algorithm
  y := x * 2;
end myFunction;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Single(parseTree.class_definition());
    }

    [Fact]
    public void Parse_Package_ParsesCorrectly()
    {
        // Arrange
        var code = @"
package MyPackage
  model Model1
    Real x;
  end Model1;
end MyPackage;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Single(parseTree.class_definition());
    }

    [Fact]
    public void ParseWithTokens_ReturnsParseTreeAndTokens()
    {
        // Arrange
        var code = @"
model TestModel
  Real x;
end TestModel;";

        // Act
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(tokenStream);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void ParseWithTokens_TokenStreamContainsTokens()
    {
        // Arrange
        var code = @"
model TestModel
  Real x;
end TestModel;";

        // Act
        var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(code);

        // Assert
        Assert.NotNull(tokenStream);
        Assert.True(tokenStream.Size > 0);
    }

    [Fact]
    public void ExtractModels_SimpleModel_ExtractsCorrectly()
    {
        // Arrange
        var code = @"
model SimpleModel
  Real x;
end SimpleModel;";

        // Act
        var models = ModelicaParserHelper.ExtractModels(code);

        // Assert
        Assert.Single(models);
        Assert.Equal("SimpleModel", models[0].Name);
        Assert.Equal("model", models[0].ClassType);
    }

    [Fact]
    public void ExtractModels_MultipleModels_ExtractsAll()
    {
        // Arrange
        var code = @"
model Model1
  Real x;
end Model1;

model Model2
  Real y;
end Model2;

model Model3
  Real z;
end Model3;";

        // Act
        var models = ModelicaParserHelper.ExtractModels(code);

        // Assert
        Assert.Equal(3, models.Count);
        Assert.Equal("Model1", models[0].Name);
        Assert.Equal("Model2", models[1].Name);
        Assert.Equal("Model3", models[2].Name);
    }

    [Fact]
    public void ExtractModels_NestedModels_ExtractsAll()
    {
        // Arrange
        var code = @"
model OuterModel
  Real x;

  model InnerModel
    Real y;
  end InnerModel;
end OuterModel;";

        // Act
        var models = ModelicaParserHelper.ExtractModels(code);

        // Assert
        Assert.Equal(2, models.Count);
        Assert.Equal("OuterModel", models[0].Name);
        Assert.Equal("InnerModel", models[1].Name);
        Assert.True(models[1].IsNested);
    }

    [Fact]
    public void ExtractModels_EmptyCode_ReturnsEmptyList()
    {
        // Arrange
        var code = "";

        // Act
        var models = ModelicaParserHelper.ExtractModels(code);

        // Assert
        Assert.Empty(models);
    }

    [Fact]
    public void ExtractModels_OnlyComments_ReturnsEmptyList()
    {
        // Arrange
        var code = @"
// Just a comment
/* Another comment */";

        // Act
        var models = ModelicaParserHelper.ExtractModels(code);

        // Assert
        Assert.Empty(models);
    }

    [Fact]
    public void ExtractModels_DifferentClassTypes_ExtractsAll()
    {
        // Arrange
        var code = @"
model MyModel
  Real x;
end MyModel;

function myFunction
  input Real x;
  output Real y;
algorithm
  y := x * 2;
end myFunction;

block MyBlock
  Real x;
end MyBlock;";

        // Act
        var models = ModelicaParserHelper.ExtractModels(code);

        // Assert
        Assert.Equal(3, models.Count);
        Assert.Equal("model", models[0].ClassType);
        Assert.Equal("function", models[1].ClassType);
        Assert.Equal("block", models[2].ClassType);
    }

    [Fact]
    public void Parse_WithAnnotations_ParsesCorrectly()
    {
        // Arrange
        var code = @"
model AnnotatedModel
  Real x annotation(Documentation(info=""<html>Test</html>""));
  annotation(Icon(graphics={Rectangle(extent={{-100,100},{100,-100}})}));
end AnnotatedModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithEquations_ParsesCorrectly()
    {
        // Arrange
        var code = @"
model EquationModel
  Real x;
  Real y;
equation
  der(x) = -x;
  y = sin(x);
end EquationModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithAlgorithms_ParsesCorrectly()
    {
        // Arrange
        var code = @"
model AlgorithmModel
  Real x;
  Real y;
algorithm
  x := 1.0;
  y := x + 2.0;
end AlgorithmModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithConnectors_ParsesCorrectly()
    {
        // Arrange
        var code = @"
connector MyConnector
  Real value;
  flow Real flowRate;
end MyConnector;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Single(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithRecord_ParsesCorrectly()
    {
        // Arrange
        var code = @"
record MyRecord
  Real x;
  Real y;
  String name;
end MyRecord;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Single(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithType_ParsesCorrectly()
    {
        // Arrange
        var code = @"
type Voltage = Real(unit=""V"");";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Single(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithExtends_ParsesCorrectly()
    {
        // Arrange
        var code = @"
model DerivedModel
  extends BaseModel;
  Real additionalVariable;
end DerivedModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    [Fact]
    public void Parse_WithImports_ParsesCorrectly()
    {
        // Arrange
        var code = @"
model ImportingModel
  import Modelica.Math.*;
  import SI = Modelica.SIunits;
  Real x;
end ImportingModel;";

        // Act
        var parseTree = ModelicaParserHelper.Parse(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotNull(parseTree.class_definition());
    }

    // ============================================================================
    // ParseWithErrors tests
    // ============================================================================

    [Fact]
    public void ParseWithErrors_ValidCode_ReturnsEmptyErrors()
    {
        // Arrange
        var code = """
model SimpleModel "test"
  Real x;
equation
  x = 1.0;
end SimpleModel;
""";

        // Act
        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Empty(errors);
    }

    [Fact]
    public void ParseWithErrors_ValidCodeWithSemicolon_HandlesExistingSemicolon()
    {
        // Arrange - code that already ends with semicolon
        var code = "model X\nequation\n  x = 1;\nend X;";

        // Act
        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(code);

        // Assert
        Assert.NotNull(parseTree);
    }

    [Fact]
    public void ParseWithErrors_ValidCodeWithoutSemicolon_AddsSemicolon()
    {
        // Arrange - code without trailing semicolon
        var code = "model X\nReal x;\nequation\n  x = 1.0;\nend X";

        // Act
        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(code);

        // Assert
        Assert.NotNull(parseTree);
    }

    [Fact]
    public void ParseWithErrors_InvalidSyntax_ReturnsErrors()
    {
        // Arrange - syntactically broken Modelica
        var code = "model @@BadModel Real x; end;";

        // Act
        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ParseWithErrors_CrlfLineEndings_NormalizesCorrectly()
    {
        // Arrange - CRLF line endings should be normalized
        var code = "model CrlfModel \"test\"\r\n  Real x;\r\nequation\r\n  x = 1.0;\r\nend CrlfModel;";

        // Act
        var (parseTree, errors) = ModelicaParserHelper.ParseWithErrors(code);

        // Assert
        Assert.NotNull(parseTree);
        Assert.Empty(errors);
    }
}
