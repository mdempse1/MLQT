using Xunit;
using ModelicaGraph.DataTypes;
using ModelicaGraph.Interfaces;

namespace ModelicaGraph.Tests;

public class GraphNodeTests
{
    // Create a concrete implementation of GraphNode for testing
    private class TestGraphNode : GraphNode
    {
        public TestGraphNode(string id, NodeType nodeType, string name)
            : base(id, nodeType, name)
        {
        }
    }

    [Fact]
    public void Constructor_SetsAllProperties()
    {
        // Arrange & Act
        var node = new TestGraphNode("test-id", NodeType.Model, "TestName");

        // Assert
        Assert.Equal("test-id", node.Id);
        Assert.Equal(NodeType.Model, node.NodeType);
        Assert.Equal("TestName", node.Name);
    }

    [Fact]
    public void Name_CanBeChanged()
    {
        // Arrange
        var node = new TestGraphNode("test-id", NodeType.File, "OriginalName");

        // Act
        node.Name = "NewName";

        // Assert
        Assert.Equal("NewName", node.Name);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var node = new TestGraphNode("test-id", NodeType.Model, "TestModel");

        // Act
        var result = node.ToString();

        // Assert
        Assert.Contains("Model", result);
        Assert.Contains("TestModel", result);
        Assert.Contains("test-id", result);
    }

    [Fact]
    public void Equals_WithSameId_ReturnsTrue()
    {
        // Arrange
        var node1 = new TestGraphNode("same-id", NodeType.Model, "Name1");
        var node2 = new TestGraphNode("same-id", NodeType.File, "Name2");

        // Act & Assert
        Assert.True(node1.Equals(node2));
        Assert.True(node2.Equals(node1));
    }

    [Fact]
    public void Equals_WithDifferentId_ReturnsFalse()
    {
        // Arrange
        var node1 = new TestGraphNode("id1", NodeType.Model, "Name");
        var node2 = new TestGraphNode("id2", NodeType.Model, "Name");

        // Act & Assert
        Assert.False(node1.Equals(node2));
        Assert.False(node2.Equals(node1));
    }

    [Fact]
    public void Equals_WithNull_ReturnsFalse()
    {
        // Arrange
        var node = new TestGraphNode("id", NodeType.Model, "Name");

        // Act & Assert
        Assert.False(node.Equals(null));
    }

    [Fact]
    public void Equals_WithDifferentType_ReturnsFalse()
    {
        // Arrange
        var node = new TestGraphNode("id", NodeType.Model, "Name");
        var other = "string";

        // Act & Assert
        Assert.False(node.Equals(other));
    }

    [Fact]
    public void GetHashCode_WithSameId_ReturnsSameHashCode()
    {
        // Arrange
        var node1 = new TestGraphNode("same-id", NodeType.Model, "Name1");
        var node2 = new TestGraphNode("same-id", NodeType.File, "Name2");

        // Act
        var hash1 = node1.GetHashCode();
        var hash2 = node2.GetHashCode();

        // Assert
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void GetHashCode_WithDifferentId_ReturnsDifferentHashCode()
    {
        // Arrange
        var node1 = new TestGraphNode("id1", NodeType.Model, "Name");
        var node2 = new TestGraphNode("id2", NodeType.Model, "Name");

        // Act
        var hash1 = node1.GetHashCode();
        var hash2 = node2.GetHashCode();

        // Assert
        Assert.NotEqual(hash1, hash2);
    }

}
