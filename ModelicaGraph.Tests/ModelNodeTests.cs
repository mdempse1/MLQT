using Xunit;
using ModelicaGraph.DataTypes;
using ModelicaGraph.Interfaces;

namespace ModelicaGraph.Tests;

public class ModelNodeTests
{
    [Fact]
    public void Constructor_WithNameAndCode_SetsProperties()
    {
        // Arrange & Act
        var node = new ModelNode("model1", "TestModel", "model TestModel\nend TestModel;");

        // Assert
        Assert.Equal("model1", node.Id);
        Assert.Equal("TestModel", node.Name);
        Assert.Equal("TestModel", node.Definition.Name);
        Assert.Equal("model TestModel\nend TestModel;", node.Definition.ModelicaCode);
        Assert.Equal(NodeType.Model, node.NodeType);
        Assert.Empty(node.UsedModelIds);
        Assert.Empty(node.UsedByModelIds);
        Assert.Null(node.ContainingFileId);
    }

    [Fact]
    public void Constructor_WithModelDefinition_SetsProperties()
    {
        // Arrange
        var definition = new ModelDefinition("TestModel", "model TestModel\nend TestModel;");

        // Act
        var node = new ModelNode("model1", definition);

        // Assert
        Assert.Equal("model1", node.Id);
        Assert.Equal("TestModel", node.Name);
        Assert.Same(definition, node.Definition);
    }

    [Fact]
    public void AddUsedModel_AddsModelToList()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");

        // Act
        node.AddUsedModel("model2");
        node.AddUsedModel("model3");

        // Assert
        Assert.Equal(2, node.UsedModelIds.Count);
        Assert.Contains("model2", node.UsedModelIds);
        Assert.Contains("model3", node.UsedModelIds);
    }

    [Fact]
    public void AddUsedModel_WithDuplicate_DoesNotAddTwice()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");

        // Act
        node.AddUsedModel("model2");
        node.AddUsedModel("model2");

        // Assert
        Assert.Single(node.UsedModelIds);
    }

    [Fact]
    public void RemoveUsedModel_RemovesModel()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");
        node.AddUsedModel("model2");
        node.AddUsedModel("model3");

        // Act
        node.RemoveUsedModel("model2");

        // Assert
        Assert.Single(node.UsedModelIds);
        Assert.DoesNotContain("model2", node.UsedModelIds);
        Assert.Contains("model3", node.UsedModelIds);
    }

    [Fact]
    public void AddUsedByModel_AddsModelToList()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");

        // Act
        node.AddUsedByModel("model2");
        node.AddUsedByModel("model3");

        // Assert
        Assert.Equal(2, node.UsedByModelIds.Count);
        Assert.Contains("model2", node.UsedByModelIds);
        Assert.Contains("model3", node.UsedByModelIds);
    }

    [Fact]
    public void AddUsedByModel_WithDuplicate_DoesNotAddTwice()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");

        // Act
        node.AddUsedByModel("model2");
        node.AddUsedByModel("model2");

        // Assert
        Assert.Single(node.UsedByModelIds);
    }

    [Fact]
    public void RemoveUsedByModel_RemovesModel()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");
        node.AddUsedByModel("model2");
        node.AddUsedByModel("model3");

        // Act
        node.RemoveUsedByModel("model2");

        // Assert
        Assert.Single(node.UsedByModelIds);
        Assert.DoesNotContain("model2", node.UsedByModelIds);
        Assert.Contains("model3", node.UsedByModelIds);
    }

    [Fact]
    public void ContainingFileId_CanBeSet()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");

        // Act
        node.ContainingFileId = "file1";

        // Assert
        Assert.Equal("file1", node.ContainingFileId);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");
        node.AddUsedModel("model2");
        node.AddUsedModel("model3");
        node.AddUsedByModel("model4");

        // Act
        var str = node.ToString();

        // Assert
        Assert.Contains("Model1", str);
        Assert.Contains("Uses: 2", str);
        Assert.Contains("UsedBy: 1", str);
    }

    [Fact]
    public void TypedProperties_CanBeSetAndRetrieved()
    {
        // Arrange
        var node = new ModelNode("model1", "Model1");

        // Act
        node.ClassType = "package";
        node.StartLine = 10;
        node.IsNested = true;

        // Assert
        Assert.Equal("package", node.ClassType);
        Assert.Equal(10, node.StartLine);
        Assert.True(node.IsNested);
    }
}
