using ModelicaGraph.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.Tests;

/// <summary>
/// Comprehensive tests for ModelDefinition class.
/// </summary>
public class ModelDefinitionTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithNameOnly_SetsNameAndEmptyCode()
    {
        // Arrange & Act
        var definition = new ModelDefinition("TestModel");

        // Assert
        Assert.Equal("TestModel", definition.Name);
        Assert.Equal("", definition.ModelicaCode);
        Assert.Null(definition.ParsedCode);
    }

    [Fact]
    public void Constructor_WithNameAndCode_SetsProperties()
    {
        // Arrange
        var name = "SimpleModel";
        var code = "model SimpleModel\n  Real x;\nend SimpleModel;";

        // Act
        var definition = new ModelDefinition(name, code);

        // Assert
        Assert.Equal(name, definition.Name);
        Assert.Equal(code, definition.ModelicaCode);
        Assert.Null(definition.ParsedCode);
    }

    [Fact]
    public void Constructor_WithEmptyName_AllowsEmptyName()
    {
        // Arrange & Act
        var definition = new ModelDefinition("");

        // Assert
        Assert.Equal("", definition.Name);
        Assert.Equal("", definition.ModelicaCode);
    }

    [Fact]
    public void Constructor_WithEmptyCode_SetsEmptyCode()
    {
        // Arrange & Act
        var definition = new ModelDefinition("TestModel", "");

        // Assert
        Assert.Equal("TestModel", definition.Name);
        Assert.Equal("", definition.ModelicaCode);
    }

    [Fact]
    public void Constructor_WithLongCode_SetsLongCode()
    {
        // Arrange
        var name = "ComplexModel";
        var code = @"
model ComplexModel
  Real x, y, z;
  parameter Real p = 1.0;
equation
  x = p * y;
  z = x + y;
end ComplexModel;";

        // Act
        var definition = new ModelDefinition(name, code);

        // Assert
        Assert.Equal(name, definition.Name);
        Assert.Equal(code, definition.ModelicaCode);
        Assert.True(definition.ModelicaCode.Length > 50);
    }

    #endregion

    #region Property Tests

    [Fact]
    public void Name_CanBeSet()
    {
        // Arrange
        var definition = new ModelDefinition("OriginalName");

        // Act
        definition.Name = "NewName";

        // Assert
        Assert.Equal("NewName", definition.Name);
    }

    [Fact]
    public void Name_CanBeSetToEmpty()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel");

        // Act
        definition.Name = "";

        // Assert
        Assert.Equal("", definition.Name);
    }

    [Fact]
    public void ModelicaCode_CanBeSet()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "original code");

        // Act
        definition.ModelicaCode = "new code";

        // Assert
        Assert.Equal("new code", definition.ModelicaCode);
    }

    [Fact]
    public void ModelicaCode_CanBeSetToEmpty()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "some code");

        // Act
        definition.ModelicaCode = "";

        // Assert
        Assert.Equal("", definition.ModelicaCode);
    }

    [Fact]
    public void ModelicaCode_CanBeSetToNull()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "some code");

        // Act
        definition.ModelicaCode = null!;

        // Assert
        Assert.Null(definition.ModelicaCode);
    }

    [Fact]
    public void ParsedCode_StartsAsNull()
    {
        // Arrange & Act
        var definition = new ModelDefinition("TestModel");

        // Assert
        Assert.Null(definition.ParsedCode);
    }

    [Fact]
    public void ParsedCode_CanBeSet()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "model TestModel end TestModel;");
        var parsedCode = ModelicaParserHelper.Parse("model TestModel end TestModel;");

        // Act
        definition.ParsedCode = parsedCode;

        // Assert
        Assert.NotNull(definition.ParsedCode);
        Assert.Same(parsedCode, definition.ParsedCode);
    }

    [Fact]
    public void ParsedCode_CanBeSetToNull()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel");
        var parsedCode = ModelicaParserHelper.Parse("model TestModel end TestModel;");
        definition.ParsedCode = parsedCode;

        // Act
        definition.ParsedCode = null;

        // Assert
        Assert.Null(definition.ParsedCode);
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void ToString_WithEmptyCode_ReturnsCorrectFormat()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel");

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Equal("Model: TestModel (0 chars)", result);
    }

    [Fact]
    public void ToString_WithShortCode_ReturnsCorrectFormat()
    {
        // Arrange
        var code = "model TestModel end TestModel;";
        var definition = new ModelDefinition("TestModel", code);

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Contains("Model: TestModel", result);
        Assert.Contains($"{code.Length} chars", result);
    }

    [Fact]
    public void ToString_WithLongCode_ReturnsCorrectFormat()
    {
        // Arrange
        var code = new string('a', 500);
        var definition = new ModelDefinition("LongModel", code);

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Equal("Model: LongModel (500 chars)", result);
    }

    [Fact]
    public void ToString_WithEmptyName_ReturnsCorrectFormat()
    {
        // Arrange
        var definition = new ModelDefinition("", "some code");

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Contains("Model: ", result);
        Assert.Contains("9 chars", result);
    }

    [Fact]
    public void ToString_AfterNameChange_ReturnsUpdatedFormat()
    {
        // Arrange
        var definition = new ModelDefinition("OldName", "code");
        definition.Name = "NewName";

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Contains("Model: NewName", result);
    }

    [Fact]
    public void ToString_AfterCodeChange_ReturnsUpdatedLength()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "short");
        var newCode = "much longer code string";
        definition.ModelicaCode = newCode;

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Contains($"{newCode.Length} chars", result);
    }

    #endregion

    #region Edge Case Tests

    [Fact]
    public void ModelDefinition_WithSpecialCharactersInName_HandlesCorrectly()
    {
        // Arrange & Act
        var definition = new ModelDefinition("Test_Model$123");

        // Assert
        Assert.Equal("Test_Model$123", definition.Name);
    }

    [Fact]
    public void ModelDefinition_WithUnicodeInName_HandlesCorrectly()
    {
        // Arrange & Act
        var definition = new ModelDefinition("Modèl™");

        // Assert
        Assert.Equal("Modèl™", definition.Name);
    }

    [Fact]
    public void ModelDefinition_WithMultilineCode_HandlesCorrectly()
    {
        // Arrange
        var code = "line1\nline2\nline3";

        // Act
        var definition = new ModelDefinition("TestModel", code);

        // Assert
        Assert.Equal(code, definition.ModelicaCode);
        Assert.Contains("\n", definition.ModelicaCode);
    }

    [Fact]
    public void ModelDefinition_WithTabsAndSpaces_PreservesWhitespace()
    {
        // Arrange
        var code = "model Test\n\t  Real x;\nend Test;";

        // Act
        var definition = new ModelDefinition("TestModel", code);

        // Assert
        Assert.Equal(code, definition.ModelicaCode);
        Assert.Contains("\t", definition.ModelicaCode);
    }

    [Fact]
    public void ModelDefinition_WithWindowsLineEndings_PreservesLineEndings()
    {
        // Arrange
        var code = "line1\r\nline2\r\nline3";

        // Act
        var definition = new ModelDefinition("TestModel", code);

        // Assert
        Assert.Equal(code, definition.ModelicaCode);
        Assert.Contains("\r\n", definition.ModelicaCode);
    }

    #endregion

    #region Integration with Parser Tests

    [Fact]
    public void ParsedCode_WithValidModelicaCode_CanBeParsed()
    {
        // Arrange
        var code = "model SimpleModel Real x; end SimpleModel;";
        var definition = new ModelDefinition("SimpleModel", code);

        // Act
        var parsed = ModelicaParserHelper.Parse(code);
        definition.ParsedCode = parsed;

        // Assert
        Assert.NotNull(definition.ParsedCode);
        Assert.NotNull(definition.ParsedCode.children);
    }

    [Fact]
    public void ParsedCode_WithComplexModelicaCode_CanBeParsed()
    {
        // Arrange
        var code = @"
model ComplexModel
  parameter Real p = 1.0;
  Real x, y;
equation
  x = p * y;
end ComplexModel;";
        var definition = new ModelDefinition("ComplexModel", code);

        // Act
        var parsed = ModelicaParserHelper.Parse(code);
        definition.ParsedCode = parsed;

        // Assert
        Assert.NotNull(definition.ParsedCode);
    }

    [Fact]
    public void ParsedCode_WithWithinStatement_CanBeParsed()
    {
        // Arrange
        var code = @"
within TestPackage;
model TestModel
  Real x;
end TestModel;";
        var definition = new ModelDefinition("TestModel", code);

        // Act
        var parsed = ModelicaParserHelper.Parse(code);
        definition.ParsedCode = parsed;

        // Assert
        Assert.NotNull(definition.ParsedCode);
    }

    #endregion

    #region Multiple Instances Tests

    [Fact]
    public void MultipleInstances_HaveIndependentProperties()
    {
        // Arrange & Act
        var def1 = new ModelDefinition("Model1", "code1");
        var def2 = new ModelDefinition("Model2", "code2");

        // Assert
        Assert.Equal("Model1", def1.Name);
        Assert.Equal("Model2", def2.Name);
        Assert.Equal("code1", def1.ModelicaCode);
        Assert.Equal("code2", def2.ModelicaCode);
    }

    [Fact]
    public void MultipleInstances_ChangingOne_DoesNotAffectOther()
    {
        // Arrange
        var def1 = new ModelDefinition("Model1", "code1");
        var def2 = new ModelDefinition("Model2", "code2");

        // Act
        def1.Name = "ChangedName";
        def1.ModelicaCode = "changed code";

        // Assert
        Assert.Equal("ChangedName", def1.Name);
        Assert.Equal("Model2", def2.Name);
        Assert.Equal("changed code", def1.ModelicaCode);
        Assert.Equal("code2", def2.ModelicaCode);
    }

    #endregion

    #region Code Length Tests

    [Fact]
    public void ModelicaCode_WithVeryLongCode_HandlesCorrectly()
    {
        // Arrange
        var longCode = new string('x', 100000); // 100K characters
        var definition = new ModelDefinition("LargeModel", longCode);

        // Act
        var length = definition.ModelicaCode.Length;
        var toString = definition.ToString();

        // Assert
        Assert.Equal(100000, length);
        Assert.Contains("100000 chars", toString);
    }

    [Fact]
    public void ModelicaCode_WithSingleCharacter_HandlesCorrectly()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "x");

        // Act
        var result = definition.ToString();

        // Assert
        Assert.Equal("Model: TestModel (1 chars)", result);
    }

    #endregion

    #region Modelica Specific Code Tests

    [Fact]
    public void ModelDefinition_WithFunctionCode_StoresCorrectly()
    {
        // Arrange
        var code = @"
function myFunc
  input Real x;
  output Real y;
algorithm
  y := x * 2;
end myFunc;";

        // Act
        var definition = new ModelDefinition("myFunc", code);

        // Assert
        Assert.Equal("myFunc", definition.Name);
        Assert.Contains("function myFunc", definition.ModelicaCode);
        Assert.Contains("algorithm", definition.ModelicaCode);
    }

    [Fact]
    public void ModelDefinition_WithBlockCode_StoresCorrectly()
    {
        // Arrange
        var code = @"
block MyBlock
  input Real u;
  output Real y;
equation
  y = u * 2;
end MyBlock;";

        // Act
        var definition = new ModelDefinition("MyBlock", code);

        // Assert
        Assert.Equal("MyBlock", definition.Name);
        Assert.Contains("block MyBlock", definition.ModelicaCode);
    }

    [Fact]
    public void ModelDefinition_WithConnectorCode_StoresCorrectly()
    {
        // Arrange
        var code = @"
connector Pin
  Real v;
  flow Real i;
end Pin;";

        // Act
        var definition = new ModelDefinition("Pin", code);

        // Assert
        Assert.Equal("Pin", definition.Name);
        Assert.Contains("connector Pin", definition.ModelicaCode);
        Assert.Contains("flow Real i", definition.ModelicaCode);
    }

    [Fact]
    public void ModelDefinition_WithPackageCode_StoresCorrectly()
    {
        // Arrange
        var code = @"
package MyPackage
  constant Real pi = 3.14159;
end MyPackage;";

        // Act
        var definition = new ModelDefinition("MyPackage", code);

        // Assert
        Assert.Equal("MyPackage", definition.Name);
        Assert.Contains("package MyPackage", definition.ModelicaCode);
    }

    [Fact]
    public void ModelDefinition_WithRecordCode_StoresCorrectly()
    {
        // Arrange
        var code = @"
record DataRecord
  Real x;
  Real y;
end DataRecord;";

        // Act
        var definition = new ModelDefinition("DataRecord", code);

        // Assert
        Assert.Equal("DataRecord", definition.Name);
        Assert.Contains("record DataRecord", definition.ModelicaCode);
    }

    #endregion

    #region Name Patterns Tests

    [Fact]
    public void Name_WithQualifiedName_StoresCorrectly()
    {
        // Arrange & Act
        var definition = new ModelDefinition("Package.SubPackage.Model");

        // Assert
        Assert.Equal("Package.SubPackage.Model", definition.Name);
    }

    [Fact]
    public void Name_WithNumericSuffix_HandlesCorrectly()
    {
        // Arrange & Act
        var definition = new ModelDefinition("Model123");

        // Assert
        Assert.Equal("Model123", definition.Name);
    }

    [Fact]
    public void Name_WithUnderscore_HandlesCorrectly()
    {
        // Arrange & Act
        var definition = new ModelDefinition("My_Model_Name");

        // Assert
        Assert.Equal("My_Model_Name", definition.Name);
    }

    #endregion

    #region Property Mutation Chain Tests

    [Fact]
    public void Properties_CanBeChainedAndSet()
    {
        // Arrange
        var definition = new ModelDefinition("Initial");

        // Act
        definition.Name = "First";
        definition.ModelicaCode = "code1";
        definition.Name = "Second";
        definition.ModelicaCode = "code2";
        definition.Name = "Final";

        // Assert
        Assert.Equal("Final", definition.Name);
        Assert.Equal("code2", definition.ModelicaCode);
    }

    [Fact]
    public void ParsedCode_CanBeSetMultipleTimes()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel");
        var parsed1 = ModelicaParserHelper.Parse("model Test1 end Test1;");
        var parsed2 = ModelicaParserHelper.Parse("model Test2 end Test2;");

        // Act
        definition.ParsedCode = parsed1;
        definition.ParsedCode = parsed2;

        // Assert
        Assert.Same(parsed2, definition.ParsedCode);
        Assert.NotSame(parsed1, definition.ParsedCode);
    }

    #endregion

    #region ToString Edge Cases

    [Fact]
    public void ToString_WithNullCode_HandlesGracefully()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "code");
        definition.ModelicaCode = null!;

        // Act & Assert
        // Should throw NullReferenceException or handle null
        Assert.Throws<NullReferenceException>(() => definition.ToString());
    }

    #endregion
}
