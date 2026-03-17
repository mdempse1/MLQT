using Xunit;
using ModelicaGraph.Interfaces;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Tests;

public class FileNodeTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        // Arrange & Act
        var node = new FileNode("file1", @"C:\path\to\test.mo");

        // Assert
        Assert.Equal("file1", node.Id);
        Assert.Equal(@"C:\path\to\test.mo", node.FilePath);
        Assert.Equal("test.mo", node.FileName);
        Assert.Equal(NodeType.File, node.NodeType);
        Assert.Empty(node.ContainedModelIds);
    }

    [Fact]
    public void FileName_ExtractsFromPath()
    {
        // Arrange & Act
        var node = new FileNode("file1", @"C:\Projects\Modelica\Blocks\package.mo");

        // Assert
        Assert.Equal("package.mo", node.FileName);
    }

    [Fact]
    public void AddContainedModel_AddsModelToList()
    {
        // Arrange
        var node = new FileNode("file1", "test.mo");

        // Act
        node.AddContainedModel("model1");
        node.AddContainedModel("model2");

        // Assert
        Assert.Equal(2, node.ContainedModelIds.Count);
        Assert.Contains("model1", node.ContainedModelIds);
        Assert.Contains("model2", node.ContainedModelIds);
    }

    [Fact]
    public void AddContainedModel_WithDuplicate_DoesNotAddTwice()
    {
        // Arrange
        var node = new FileNode("file1", "test.mo");

        // Act
        node.AddContainedModel("model1");
        node.AddContainedModel("model1");

        // Assert
        Assert.Single(node.ContainedModelIds);
        Assert.Contains("model1", node.ContainedModelIds);
    }

    [Fact]
    public void Content_CanBeSet()
    {
        // Arrange
        var node = new FileNode("file1", "test.mo");
        var content = "model Test\nend Test;";

        // Act
        node.Content = content;

        // Assert
        Assert.Equal(content, node.Content);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var node = new FileNode("file1", "test.mo");
        node.AddContainedModel("model1");
        node.AddContainedModel("model2");

        // Act
        var str = node.ToString();

        // Assert
        Assert.Contains("test.mo", str);
        Assert.Contains("2 models", str);
    }

}
