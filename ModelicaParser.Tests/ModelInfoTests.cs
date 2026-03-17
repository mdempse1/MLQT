using Xunit;
using ModelicaParser.DataTypes;

namespace ModelicaParser.Tests;

public class ModelInfoTests
{
    [Fact]
    public void Constructor_SetsRequiredProperties()
    {
        // Arrange & Act
        var modelInfo = new ModelInfo("TestModel", "model TestModel end TestModel;", "model");

        // Assert
        Assert.Equal("TestModel", modelInfo.Name);
        Assert.Equal("model TestModel end TestModel;", modelInfo.SourceCode);
        Assert.Equal("model", modelInfo.ClassType);
    }

    [Fact]
    public void Constructor_DefaultValues_SetCorrectly()
    {
        // Arrange & Act
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Assert
        Assert.Equal(0, modelInfo.StartLine);
        Assert.Equal(0, modelInfo.StopLine);
        Assert.False(modelInfo.IsNested);
        Assert.Null(modelInfo.ParentModelName);
        Assert.True(modelInfo.CanBeStoredStandalone);
    }

    [Fact]
    public void Name_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Original", "code", "model");

        // Act
        modelInfo.Name = "Modified";

        // Assert
        Assert.Equal("Modified", modelInfo.Name);
    }

    [Fact]
    public void SourceCode_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "original code", "model");

        // Act
        modelInfo.SourceCode = "modified code";

        // Assert
        Assert.Equal("modified code", modelInfo.SourceCode);
    }

    [Fact]
    public void ClassType_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.ClassType = "function";

        // Assert
        Assert.Equal("function", modelInfo.ClassType);
    }

    [Fact]
    public void StartLine_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.StartLine = 10;

        // Assert
        Assert.Equal(10, modelInfo.StartLine);
    }

    [Fact]
    public void StopLine_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.StopLine = 20;

        // Assert
        Assert.Equal(20, modelInfo.StopLine);
    }

    [Fact]
    public void IsNested_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.IsNested = true;

        // Assert
        Assert.True(modelInfo.IsNested);
    }

    [Fact]
    public void ParentModelName_CanBeSet()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.ParentModelName = "ParentModel";

        // Assert
        Assert.Equal("ParentModel", modelInfo.ParentModelName);
    }

    [Fact]
    public void CanBeStoredStandalone_DefaultsToTrue()
    {
        // Arrange & Act
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Assert
        Assert.True(modelInfo.CanBeStoredStandalone);
    }

    [Fact]
    public void CanBeStoredStandalone_CanBeSetToFalse()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.CanBeStoredStandalone = false;

        // Assert
        Assert.False(modelInfo.CanBeStoredStandalone);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var modelInfo = new ModelInfo("TestModel", "code", "model")
        {
            StartLine = 5,
            StopLine = 15
        };

        // Act
        var result = modelInfo.ToString();

        // Assert
        Assert.Equal("model TestModel (Lines 5-15)", result);
    }

    [Fact]
    public void ToString_WithFunction_IncludesClassType()
    {
        // Arrange
        var modelInfo = new ModelInfo("myFunction", "code", "function")
        {
            StartLine = 1,
            StopLine = 10
        };

        // Act
        var result = modelInfo.ToString();

        // Assert
        Assert.Equal("function myFunction (Lines 1-10)", result);
    }

    [Fact]
    public void ToString_WithBlock_IncludesClassType()
    {
        // Arrange
        var modelInfo = new ModelInfo("MyBlock", "code", "block")
        {
            StartLine = 20,
            StopLine = 30
        };

        // Act
        var result = modelInfo.ToString();

        // Assert
        Assert.Equal("block MyBlock (Lines 20-30)", result);
    }

    [Fact]
    public void ToString_WithPackage_IncludesClassType()
    {
        // Arrange
        var modelInfo = new ModelInfo("MyPackage", "code", "package")
        {
            StartLine = 1,
            StopLine = 100
        };

        // Act
        var result = modelInfo.ToString();

        // Assert
        Assert.Equal("package MyPackage (Lines 1-100)", result);
    }

    [Fact]
    public void ToString_WithZeroLines_ShowsZeroLines()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        var result = modelInfo.ToString();

        // Assert
        Assert.Equal("model Test (Lines 0-0)", result);
    }

    [Fact]
    public void ParentModelName_CanBeSetToNull()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model")
        {
            ParentModelName = "Parent"
        };

        // Act
        modelInfo.ParentModelName = null;

        // Assert
        Assert.Null(modelInfo.ParentModelName);
    }

    [Fact]
    public void AllProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.Name = "CompleteModel";
        modelInfo.SourceCode = "model CompleteModel end CompleteModel;";
        modelInfo.ClassType = "model";
        modelInfo.StartLine = 5;
        modelInfo.StopLine = 10;
        modelInfo.IsNested = true;
        modelInfo.ParentModelName = "OuterModel";
        modelInfo.CanBeStoredStandalone = false;

        // Assert
        Assert.Equal("CompleteModel", modelInfo.Name);
        Assert.Equal("model CompleteModel end CompleteModel;", modelInfo.SourceCode);
        Assert.Equal("model", modelInfo.ClassType);
        Assert.Equal(5, modelInfo.StartLine);
        Assert.Equal(10, modelInfo.StopLine);
        Assert.True(modelInfo.IsNested);
        Assert.Equal("OuterModel", modelInfo.ParentModelName);
        Assert.False(modelInfo.CanBeStoredStandalone);
    }

    [Fact]
    public void Constructor_WithEmptyStrings_Works()
    {
        // Arrange & Act
        var modelInfo = new ModelInfo("", "", "");

        // Assert
        Assert.Equal("", modelInfo.Name);
        Assert.Equal("", modelInfo.SourceCode);
        Assert.Equal("", modelInfo.ClassType);
    }

    [Fact]
    public void ToString_WithEmptyName_ShowsEmptyName()
    {
        // Arrange
        var modelInfo = new ModelInfo("", "code", "model")
        {
            StartLine = 1,
            StopLine = 5
        };

        // Act
        var result = modelInfo.ToString();

        // Assert
        Assert.Equal("model  (Lines 1-5)", result);
    }

    [Fact]
    public void SourceCode_WithLargeContent_StoresCorrectly()
    {
        // Arrange
        var largeCode = new string('x', 10000);
        var modelInfo = new ModelInfo("LargeModel", largeCode, "model");

        // Act & Assert
        Assert.Equal(10000, modelInfo.SourceCode.Length);
        Assert.Equal(largeCode, modelInfo.SourceCode);
    }

    [Fact]
    public void LineNumbers_CanBeNegative()
    {
        // Arrange
        var modelInfo = new ModelInfo("Test", "code", "model");

        // Act
        modelInfo.StartLine = -1;
        modelInfo.StopLine = -10;

        // Assert
        Assert.Equal(-1, modelInfo.StartLine);
        Assert.Equal(-10, modelInfo.StopLine);
    }

    [Fact]
    public void ParentModelName_WithNestedPath_StoresCorrectly()
    {
        // Arrange
        var modelInfo = new ModelInfo("InnerModel", "code", "model");

        // Act
        modelInfo.ParentModelName = "Package.SubPackage.OuterModel";

        // Assert
        Assert.Equal("Package.SubPackage.OuterModel", modelInfo.ParentModelName);
    }

    [Fact]
    public void Name_WithSpecialCharacters_StoresCorrectly()
    {
        // Arrange & Act
        var modelInfo = new ModelInfo("Model_123", "code", "model");

        // Assert
        Assert.Equal("Model_123", modelInfo.Name);
    }

    [Fact]
    public void ClassType_WithAllValidTypes_WorksCorrectly()
    {
        // Test all common class types
        var types = new[] { "model", "function", "block", "connector", "record", "type", "package", "class" };

        foreach (var type in types)
        {
            var modelInfo = new ModelInfo("Test", "code", type);
            Assert.Equal(type, modelInfo.ClassType);
        }
    }

    [Fact]
    public void ToString_WithMultilineSourceCode_OnlyShowsMetadata()
    {
        // Arrange
        var multilineCode = @"model Test
  Real x;
  Real y;
end Test;";
        var modelInfo = new ModelInfo("Test", multilineCode, "model")
        {
            StartLine = 1,
            StopLine = 4
        };

        // Act
        var result = modelInfo.ToString();

        // Assert
        // ToString should not include source code, only metadata
        Assert.Equal("model Test (Lines 1-4)", result);
        Assert.DoesNotContain("Real x", result);
    }
}
