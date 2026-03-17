using Xunit;
using ModelicaGraph.Interfaces;
using ModelicaGraph.DataTypes;

namespace ModelicaGraph.Tests;

public class DirectedGraphTests
{
    [Fact]
    public void Constructor_CreatesEmptyGraph()
    {
        // Arrange & Act
        var graph = new DirectedGraph();

        // Assert
        Assert.Equal(0, graph.NodeCount);
        Assert.Empty(graph.FileNodes);
        Assert.Empty(graph.ModelNodes);
    }

    [Fact]
    public void AddNode_AddsNodeToGraph()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node = new FileNode("file1", "test.mo");

        // Act
        graph.AddNode(node);

        // Assert
        Assert.Equal(1, graph.NodeCount);
        Assert.Contains(node, graph.FileNodes);
    }

    [Fact]
    public void AddNode_WithDuplicateId_DoesNotAddTwice()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new FileNode("file1", "test.mo");
        var node2 = new FileNode("file1", "test2.mo");

        // Act
        graph.AddNode(node1);
        graph.AddNode(node2);

        // Assert
        Assert.Equal(1, graph.NodeCount);
    }

    [Fact]
    public void RemoveNode_RemovesExistingNode()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node = new FileNode("file1", "test.mo");
        graph.AddNode(node);

        // Act
        var result = graph.RemoveNode("file1");

        // Assert
        Assert.True(result);
        Assert.Equal(0, graph.NodeCount);
    }

    [Fact]
    public void RemoveNode_WithNonExistentNode_ReturnsFalse()
    {
        // Arrange
        var graph = new DirectedGraph();

        // Act
        var result = graph.RemoveNode("nonexistent");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RemoveNode_RemovesAllEdgesToAndFromNode()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        var node3 = new ModelNode("model3", "Model3");
        graph.AddNode(node1);
        graph.AddNode(node2);
        graph.AddNode(node3);
        graph.AddEdge("model1", "model2");
        graph.AddEdge("model2", "model3");
        graph.AddEdge("model3", "model1");

        // Act
        graph.RemoveNode("model2");

        // Assert
        Assert.Empty(graph.GetOutgoingNodes("model1"));
        Assert.Empty(graph.GetIncomingNodes("model3"));
    }

    [Fact]
    public void GetNode_ReturnsNodeById()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node = new FileNode("file1", "test.mo");
        graph.AddNode(node);

        // Act
        var retrieved = graph.GetNode("file1");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Same(node, retrieved);
    }

    [Fact]
    public void GetNode_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        var graph = new DirectedGraph();

        // Act
        var retrieved = graph.GetNode("nonexistent");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void GetNode_Generic_ReturnsTypedNode()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        var modelNode = new ModelNode("model1", "Model1");
        graph.AddNode(fileNode);
        graph.AddNode(modelNode);

        // Act
        var retrievedFile = graph.GetNode<FileNode>("file1");
        var retrievedModel = graph.GetNode<ModelNode>("model1");

        // Assert
        Assert.NotNull(retrievedFile);
        Assert.NotNull(retrievedModel);
        Assert.IsType<FileNode>(retrievedFile);
        Assert.IsType<ModelNode>(retrievedModel);
    }

    [Fact]
    public void GetNode_Generic_WithWrongType_ReturnsDefault()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        graph.AddNode(fileNode);

        // Act
        var retrieved = graph.GetNode<ModelNode>("file1");

        // Assert
        Assert.Null(retrieved);
    }

    [Fact]
    public void HasNode_WithExistingNode_ReturnsTrue()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node = new FileNode("file1", "test.mo");
        graph.AddNode(node);

        // Act
        var exists = graph.HasNode("file1");

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public void HasNode_WithNonExistentNode_ReturnsFalse()
    {
        // Arrange
        var graph = new DirectedGraph();

        // Act
        var exists = graph.HasNode("nonexistent");

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public void AddEdge_CreatesDirectedEdge()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        graph.AddNode(node1);
        graph.AddNode(node2);

        // Act
        graph.AddEdge("model1", "model2");

        // Assert
        var outgoing = graph.GetOutgoingNodes("model1");
        var incoming = graph.GetIncomingNodes("model2");
        Assert.Contains(node2, outgoing);
        Assert.Contains(node1, incoming);
    }

    [Fact]
    public void AddEdge_WithNonExistentNodes_ThrowsArgumentException()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        graph.AddNode(node1);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => graph.AddEdge("model1", "nonexistent"));
        Assert.Throws<ArgumentException>(() => graph.AddEdge("nonexistent", "model1"));
    }

    [Fact]
    public void AddEdge_Duplicate_DoesNotAddTwice()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        graph.AddNode(node1);
        graph.AddNode(node2);

        // Act
        graph.AddEdge("model1", "model2");
        graph.AddEdge("model1", "model2");

        // Assert
        var outgoing = graph.GetOutgoingNodes("model1").ToList();
        Assert.Single(outgoing);
    }

    [Fact]
    public void RemoveEdge_RemovesExistingEdge()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        graph.AddNode(node1);
        graph.AddNode(node2);
        graph.AddEdge("model1", "model2");

        // Act
        var result = graph.RemoveEdge("model1", "model2");

        // Assert
        Assert.True(result);
        Assert.Empty(graph.GetOutgoingNodes("model1"));
    }

    [Fact]
    public void RemoveEdge_WithNonExistentEdge_ReturnsFalse()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        graph.AddNode(node1);
        graph.AddNode(node2);

        // Act
        var result = graph.RemoveEdge("model1", "model2");

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void GetOutgoingNodes_ReturnsConnectedNodes()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        var node3 = new ModelNode("model3", "Model3");
        graph.AddNode(node1);
        graph.AddNode(node2);
        graph.AddNode(node3);
        graph.AddEdge("model1", "model2");
        graph.AddEdge("model1", "model3");

        // Act
        var outgoing = graph.GetOutgoingNodes("model1").ToList();

        // Assert
        Assert.Equal(2, outgoing.Count);
        Assert.Contains(node2, outgoing);
        Assert.Contains(node3, outgoing);
    }

    [Fact]
    public void GetOutgoingNodes_WithNoEdges_ReturnsEmpty()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node = new ModelNode("model1", "Model1");
        graph.AddNode(node);

        // Act
        var outgoing = graph.GetOutgoingNodes("model1");

        // Assert
        Assert.Empty(outgoing);
    }

    [Fact]
    public void GetIncomingNodes_ReturnsNodesPointingToTarget()
    {
        // Arrange
        var graph = new DirectedGraph();
        var node1 = new ModelNode("model1", "Model1");
        var node2 = new ModelNode("model2", "Model2");
        var node3 = new ModelNode("model3", "Model3");
        graph.AddNode(node1);
        graph.AddNode(node2);
        graph.AddNode(node3);
        graph.AddEdge("model1", "model3");
        graph.AddEdge("model2", "model3");

        // Act
        var incoming = graph.GetIncomingNodes("model3").ToList();

        // Assert
        Assert.Equal(2, incoming.Count);
        Assert.Contains(node1, incoming);
        Assert.Contains(node2, incoming);
    }

    [Fact]
    public void AddFileContainsModel_CreatesRelationship()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        var modelNode = new ModelNode("model1", "Model1");
        graph.AddNode(fileNode);
        graph.AddNode(modelNode);

        // Act
        graph.AddFileContainsModel("file1", "model1");

        // Assert
        Assert.Contains("model1", fileNode.ContainedModelIds);
        Assert.Equal("file1", modelNode.ContainingFileId);
        Assert.Contains(modelNode, graph.GetOutgoingNodes("file1"));
    }

    [Fact]
    public void AddFileContainsModel_WithNonExistentNodes_ThrowsArgumentException()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        graph.AddNode(fileNode);

        // Act & Assert
        Assert.Throws<ArgumentException>(() => graph.AddFileContainsModel("file1", "nonexistent"));
    }

    [Fact]
    public void AddModelUsesModel_CreatesRelationship()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model1 = new ModelNode("model1", "Model1");
        var model2 = new ModelNode("model2", "Model2");
        graph.AddNode(model1);
        graph.AddNode(model2);

        // Act
        graph.AddModelUsesModel("model1", "model2");

        // Assert
        Assert.Contains("model2", model1.UsedModelIds);
        Assert.Contains("model1", model2.UsedByModelIds);
        Assert.Contains(model2, graph.GetOutgoingNodes("model1"));
    }

    [Fact]
    public void GetModelsInFile_ReturnsAllModelsInFile()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        var model1 = new ModelNode("model1", "Model1");
        var model2 = new ModelNode("model2", "Model2");
        graph.AddNode(fileNode);
        graph.AddNode(model1);
        graph.AddNode(model2);
        graph.AddFileContainsModel("file1", "model1");
        graph.AddFileContainsModel("file1", "model2");

        // Act
        var models = graph.GetModelsInFile("file1").ToList();

        // Assert
        Assert.Equal(2, models.Count);
        Assert.Contains(model1, models);
        Assert.Contains(model2, models);
    }

    [Fact]
    public void GetUsedModels_ReturnsDependencies()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model1 = new ModelNode("model1", "Model1");
        var model2 = new ModelNode("model2", "Model2");
        var model3 = new ModelNode("model3", "Model3");
        graph.AddNode(model1);
        graph.AddNode(model2);
        graph.AddNode(model3);
        graph.AddModelUsesModel("model1", "model2");
        graph.AddModelUsesModel("model1", "model3");

        // Act
        var usedModels = graph.GetUsedModels("model1").ToList();

        // Assert
        Assert.Equal(2, usedModels.Count);
        Assert.Contains(model2, usedModels);
        Assert.Contains(model3, usedModels);
    }

    [Fact]
    public void GetModelUsedBy_ReturnsReverseDependencies()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model1 = new ModelNode("model1", "Model1");
        var model2 = new ModelNode("model2", "Model2");
        var model3 = new ModelNode("model3", "Model3");
        graph.AddNode(model1);
        graph.AddNode(model2);
        graph.AddNode(model3);
        graph.AddModelUsesModel("model1", "model3");
        graph.AddModelUsesModel("model2", "model3");

        // Act
        var usedBy = graph.GetModelUsedBy("model3").ToList();

        // Assert
        Assert.Equal(2, usedBy.Count);
        Assert.Contains(model1, usedBy);
        Assert.Contains(model2, usedBy);
    }

    [Fact]
    public void Clear_RemovesAllNodesAndEdges()
    {
        // Arrange
        var graph = new DirectedGraph();
        var model1 = new ModelNode("model1", "Model1");
        var model2 = new ModelNode("model2", "Model2");
        graph.AddNode(model1);
        graph.AddNode(model2);
        graph.AddEdge("model1", "model2");

        // Act
        graph.Clear();

        // Assert
        Assert.Equal(0, graph.NodeCount);
    }

    [Fact]
    public void FileNodes_ReturnsOnlyFileNodes()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        var modelNode = new ModelNode("model1", "Model1");
        graph.AddNode(fileNode);
        graph.AddNode(modelNode);

        // Act
        var fileNodes = graph.FileNodes.ToList();

        // Assert
        Assert.Single(fileNodes);
        Assert.Contains(fileNode, fileNodes);
        Assert.DoesNotContain<IGraphNode>(modelNode, fileNodes.Cast<IGraphNode>());
    }

    [Fact]
    public void ModelNodes_ReturnsOnlyModelNodes()
    {
        // Arrange
        var graph = new DirectedGraph();
        var fileNode = new FileNode("file1", "test.mo");
        var modelNode = new ModelNode("model1", "Model1");
        graph.AddNode(fileNode);
        graph.AddNode(modelNode);

        // Act
        var modelNodes = graph.ModelNodes.ToList();

        // Assert
        Assert.Single(modelNodes);
        Assert.Contains(modelNode, modelNodes);
        Assert.DoesNotContain<IGraphNode>(fileNode, modelNodes.Cast<IGraphNode>());
    }
}
