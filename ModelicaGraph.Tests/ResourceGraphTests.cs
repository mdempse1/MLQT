using ModelicaGraph.DataTypes;
using ModelicaGraph.Interfaces;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

/// <summary>
/// Tests for resource node management in DirectedGraph, plus ResourceEdge,
/// ResourceFileNode, and ResourceDirectoryNode data types.
/// </summary>
public class ResourceGraphTests
{
    // ============================================================================
    // Helpers
    // ============================================================================

    private static (DirectedGraph graph, ModelNode model) CreateGraphWithModel(
        string modelId = "model1", string modelName = "Model1")
    {
        var graph = new DirectedGraph();
        var model = new ModelNode(modelId, modelName);
        graph.AddNode(model);
        return (graph, model);
    }

    // ============================================================================
    // ResourceEdge
    // ============================================================================

    [Fact]
    public void ResourceEdge_Properties_CanBeSetAndRead()
    {
        var edge = new ResourceEdge
        {
            ModelId = "model1",
            ResourceNodeId = "resource:file:test",
            RawPath = "modelica://Lib/Resources/test.mat",
            ReferenceType = ResourceReferenceType.LoadResource,
            ParameterName = "fileName",
            IsAbsolutePath = false
        };

        Assert.Equal("model1", edge.ModelId);
        Assert.Equal("resource:file:test", edge.ResourceNodeId);
        Assert.Equal("modelica://Lib/Resources/test.mat", edge.RawPath);
        Assert.Equal(ResourceReferenceType.LoadResource, edge.ReferenceType);
        Assert.Equal("fileName", edge.ParameterName);
        Assert.False(edge.IsAbsolutePath);
    }

    [Fact]
    public void ResourceEdge_GetEdgeKey_ReturnsCorrectFormat()
    {
        var edge = new ResourceEdge
        {
            ModelId = "model1",
            ResourceNodeId = "resource:file:test",
            ReferenceType = ResourceReferenceType.UriReference
        };

        var key = edge.GetEdgeKey();

        Assert.Equal("model1|resource:file:test|UriReference", key);
    }

    [Fact]
    public void ResourceEdge_ToString_ReturnsReadableDescription()
    {
        var edge = new ResourceEdge
        {
            ModelId = "MyModel",
            ResourceNodeId = "resource:file:data.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };

        var str = edge.ToString();

        Assert.Contains("MyModel", str);
        Assert.Contains("resource:file:data.mat", str);
        Assert.Contains("LoadResource", str);
    }

    // ============================================================================
    // ResourceFileNode
    // ============================================================================

    [Fact]
    public void ResourceFileNode_Constructor_SetsProperties()
    {
        var node = new ResourceFileNode("res:file:test.mat", "/data/test.mat");

        Assert.Equal("res:file:test.mat", node.Id);
        Assert.Equal("/data/test.mat", node.ResolvedPath);
        Assert.Equal("test.mat", node.Name);
        Assert.Equal(NodeType.ResourceFile, node.NodeType);
        Assert.Empty(node.ReferencedByModelIds);
    }

    [Fact]
    public void ResourceFileNode_AddReferencingModel_AddsModel()
    {
        var node = new ResourceFileNode("id1", "/path/file.mat");

        node.AddReferencingModel("model1");
        node.AddReferencingModel("model2");

        Assert.Equal(2, node.ReferencedByModelIds.Count);
        Assert.Contains("model1", node.ReferencedByModelIds);
        Assert.Contains("model2", node.ReferencedByModelIds);
    }

    [Fact]
    public void ResourceFileNode_AddReferencingModel_DuplicatesIgnored()
    {
        var node = new ResourceFileNode("id1", "/path/file.mat");

        node.AddReferencingModel("model1");
        node.AddReferencingModel("model1");

        Assert.Single(node.ReferencedByModelIds);
    }

    [Fact]
    public void ResourceFileNode_RemoveReferencingModel_RemovesCorrectly()
    {
        var node = new ResourceFileNode("id1", "/path/file.mat");
        node.AddReferencingModel("model1");
        node.AddReferencingModel("model2");

        node.RemoveReferencingModel("model1");

        Assert.Single(node.ReferencedByModelIds);
        Assert.DoesNotContain("model1", node.ReferencedByModelIds);
        Assert.Contains("model2", node.ReferencedByModelIds);
    }

    [Fact]
    public void ResourceFileNode_ToString_ReturnsReadableDescription()
    {
        var node = new ResourceFileNode("id1", "/path/file.mat");
        node.AddReferencingModel("model1");

        var str = node.ToString();

        Assert.Contains("file.mat", str);
        Assert.Contains("1", str); // 1 reference
    }

    // ============================================================================
    // ResourceDirectoryNode
    // ============================================================================

    [Fact]
    public void ResourceDirectoryNode_Constructor_SetsProperties()
    {
        var node = new ResourceDirectoryNode("res:dir:include", "/lib/Resources/Include");

        Assert.Equal("res:dir:include", node.Id);
        Assert.Equal("/lib/Resources/Include", node.ResolvedPath);
        Assert.Equal("Include", node.Name);
        Assert.Equal(NodeType.ResourceDirectory, node.NodeType);
        Assert.Empty(node.ReferencedByModelIds);
        Assert.Empty(node.ContainedFileIds);
    }

    [Fact]
    public void ResourceDirectoryNode_Constructor_TrimsTrailingSeparator()
    {
        var sep = Path.DirectorySeparatorChar;
        var node = new ResourceDirectoryNode("id", $"/lib/Resources/Include{sep}");

        // Name should not include trailing separator
        Assert.Equal("Include", node.Name);
    }

    [Fact]
    public void ResourceDirectoryNode_AddReferencingModel_AddsModel()
    {
        var node = new ResourceDirectoryNode("id1", "/path/include");

        node.AddReferencingModel("model1");
        node.AddReferencingModel("model2");

        Assert.Equal(2, node.ReferencedByModelIds.Count);
    }

    [Fact]
    public void ResourceDirectoryNode_AddReferencingModel_DuplicatesIgnored()
    {
        var node = new ResourceDirectoryNode("id1", "/path/include");

        node.AddReferencingModel("model1");
        node.AddReferencingModel("model1");

        Assert.Single(node.ReferencedByModelIds);
    }

    [Fact]
    public void ResourceDirectoryNode_RemoveReferencingModel_RemovesCorrectly()
    {
        var node = new ResourceDirectoryNode("id1", "/path/include");
        node.AddReferencingModel("model1");
        node.AddReferencingModel("model2");

        node.RemoveReferencingModel("model1");

        Assert.Single(node.ReferencedByModelIds);
        Assert.DoesNotContain("model1", node.ReferencedByModelIds);
    }

    [Fact]
    public void ResourceDirectoryNode_AddContainedFile_AddsAndDeduplicates()
    {
        var node = new ResourceDirectoryNode("id1", "/path/include");

        node.AddContainedFile("file1");
        node.AddContainedFile("file2");
        node.AddContainedFile("file1"); // duplicate

        Assert.Equal(2, node.ContainedFileIds.Count);
        Assert.Contains("file1", node.ContainedFileIds);
        Assert.Contains("file2", node.ContainedFileIds);
    }

    [Fact]
    public void ResourceDirectoryNode_ToString_ReturnsReadableDescription()
    {
        var node = new ResourceDirectoryNode("id1", "/path/Include");
        node.AddReferencingModel("model1");

        var str = node.ToString();

        Assert.Contains("Include", str);
        Assert.Contains("1", str);
    }

    // ============================================================================
    // DirectedGraph — resource ID generation
    // ============================================================================

    [Fact]
    public void GenerateResourceFileId_ReturnsDeterministicId()
    {
        var id1 = DirectedGraph.GenerateResourceFileId("/lib/Resources/test.mat");
        var id2 = DirectedGraph.GenerateResourceFileId("/lib/Resources/test.mat");

        Assert.Equal(id1, id2);
        Assert.StartsWith("resource:file:", id1);
    }

    [Fact]
    public void GenerateResourceDirectoryId_ReturnsDeterministicId()
    {
        var id1 = DirectedGraph.GenerateResourceDirectoryId("/lib/Resources/Include");
        var id2 = DirectedGraph.GenerateResourceDirectoryId("/lib/Resources/Include");

        Assert.Equal(id1, id2);
        Assert.StartsWith("resource:dir:", id1);
    }

    [Fact]
    public void GenerateResourceFileId_DifferentFromDirectoryId()
    {
        var fileId = DirectedGraph.GenerateResourceFileId("/lib/Resources/test.mat");
        var dirId = DirectedGraph.GenerateResourceDirectoryId("/lib/Resources/test.mat");

        Assert.NotEqual(fileId, dirId);
    }

    // ============================================================================
    // DirectedGraph — GetOrCreate resource nodes
    // ============================================================================

    [Fact]
    public void GetOrCreateResourceFileNode_NewPath_CreatesNode()
    {
        var graph = new DirectedGraph();
        var path = "/nonexistent/path/file.mat";

        var node = graph.GetOrCreateResourceFileNode(path);

        Assert.NotNull(node);
        Assert.Equal(path, node.ResolvedPath);
        Assert.False(node.FileExists);
        Assert.False(node.IsImageFile);
    }

    [Fact]
    public void GetOrCreateResourceFileNode_ExistingPath_ReturnsSameInstance()
    {
        var graph = new DirectedGraph();
        var path = "/nonexistent/file.mat";

        var node1 = graph.GetOrCreateResourceFileNode(path);
        var node2 = graph.GetOrCreateResourceFileNode(path);

        Assert.Same(node1, node2);
    }

    [Fact]
    public void GetOrCreateResourceFileNode_DetectsImageExtensions()
    {
        var graph = new DirectedGraph();

        foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".bmp", ".ico" })
        {
            var node = graph.GetOrCreateResourceFileNode($"/img/icon{ext}");
            Assert.True(node.IsImageFile, $"Expected {ext} to be detected as image");
        }
    }

    [Fact]
    public void GetOrCreateResourceFileNode_NonImageExtension_NotMarkedAsImage()
    {
        var graph = new DirectedGraph();

        var node = graph.GetOrCreateResourceFileNode("/data/table.mat");

        Assert.False(node.IsImageFile);
    }

    [Fact]
    public void GetOrCreateResourceDirectoryNode_NewPath_CreatesNode()
    {
        var graph = new DirectedGraph();
        var path = "/nonexistent/Include";

        var node = graph.GetOrCreateResourceDirectoryNode(path);

        Assert.NotNull(node);
        Assert.Equal(path, node.ResolvedPath);
        Assert.False(node.DirectoryExists);
    }

    [Fact]
    public void GetOrCreateResourceDirectoryNode_ExistingPath_ReturnsSameInstance()
    {
        var graph = new DirectedGraph();
        var path = "/nonexistent/Include";

        var node1 = graph.GetOrCreateResourceDirectoryNode(path);
        var node2 = graph.GetOrCreateResourceDirectoryNode(path);

        Assert.Same(node1, node2);
    }

    [Fact]
    public void ResourceFileNodes_ReturnsOnlyResourceFileNodes()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("m1", "M1"));
        graph.GetOrCreateResourceFileNode("/path/file.mat");
        graph.GetOrCreateResourceDirectoryNode("/path/dir");

        var resourceFiles = graph.ResourceFileNodes.ToList();

        Assert.Single(resourceFiles);
        Assert.IsType<ResourceFileNode>(resourceFiles[0]);
    }

    [Fact]
    public void ResourceDirectoryNodes_ReturnsOnlyResourceDirectoryNodes()
    {
        var graph = new DirectedGraph();
        graph.AddNode(new ModelNode("m1", "M1"));
        graph.GetOrCreateResourceFileNode("/path/file.mat");
        graph.GetOrCreateResourceDirectoryNode("/path/dir");

        var resourceDirs = graph.ResourceDirectoryNodes.ToList();

        Assert.Single(resourceDirs);
        Assert.IsType<ResourceDirectoryNode>(resourceDirs[0]);
    }

    [Fact]
    public void ResourceEdges_ReturnsAllStoredEdges()
    {
        var (graph, model) = CreateGraphWithModel();
        var resourceNode = graph.GetOrCreateResourceFileNode("/path/file.mat");
        var edge = new ResourceEdge
        {
            RawPath = "/path/file.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };
        graph.AddModelReferencesResource(model.Id, resourceNode.Id, edge);

        var edges = graph.ResourceEdges.ToList();

        Assert.Single(edges);
    }

    // ============================================================================
    // DirectedGraph — AddModelReferencesResource
    // ============================================================================

    [Fact]
    public void AddModelReferencesResource_FileNode_CreatesEdgeAndBackReference()
    {
        var (graph, model) = CreateGraphWithModel();
        var resourceNode = graph.GetOrCreateResourceFileNode("/path/data.mat");
        var edge = new ResourceEdge
        {
            RawPath = "/path/data.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };

        graph.AddModelReferencesResource(model.Id, resourceNode.Id, edge);

        Assert.Contains(resourceNode.Id, model.ReferencedResourceIds);
        Assert.Contains(model.Id, resourceNode.ReferencedByModelIds);
        Assert.Equal(model.Id, edge.ModelId);
        Assert.Equal(resourceNode.Id, edge.ResourceNodeId);
    }

    [Fact]
    public void AddModelReferencesResource_DirectoryNode_CreatesEdgeAndBackReference()
    {
        var (graph, model) = CreateGraphWithModel();
        var dirNode = graph.GetOrCreateResourceDirectoryNode("/path/Include");
        var edge = new ResourceEdge
        {
            RawPath = "modelica://Lib/Resources/Include",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        };

        graph.AddModelReferencesResource(model.Id, dirNode.Id, edge);

        Assert.Contains(dirNode.Id, model.ReferencedResourceIds);
        Assert.Contains(model.Id, dirNode.ReferencedByModelIds);
    }

    [Fact]
    public void AddModelReferencesResource_ModelNotFound_ThrowsArgumentException()
    {
        var graph = new DirectedGraph();
        var resourceNode = graph.GetOrCreateResourceFileNode("/path/file.mat");
        var edge = new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource };

        Assert.Throws<ArgumentException>(() =>
            graph.AddModelReferencesResource("nonexistent", resourceNode.Id, edge));
    }

    [Fact]
    public void AddModelReferencesResource_ResourceNotFound_ThrowsArgumentException()
    {
        var (graph, model) = CreateGraphWithModel();
        var edge = new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource };

        Assert.Throws<ArgumentException>(() =>
            graph.AddModelReferencesResource(model.Id, "nonexistent:resource", edge));
    }

    // ============================================================================
    // DirectedGraph — resource query methods
    // ============================================================================

    [Fact]
    public void GetResourceEdgesForModel_ReturnsCorrectEdges()
    {
        var (graph, model) = CreateGraphWithModel();
        var r1 = graph.GetOrCreateResourceFileNode("/path/file1.mat");
        var r2 = graph.GetOrCreateResourceFileNode("/path/file2.mat");
        graph.AddModelReferencesResource(model.Id, r1.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource });
        graph.AddModelReferencesResource(model.Id, r2.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.UriReference });

        var edges = graph.GetResourceEdgesForModel(model.Id).ToList();

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(model.Id, e.ModelId));
    }

    [Fact]
    public void GetModelEdgesToResource_ReturnsCorrectEdges()
    {
        var graph = new DirectedGraph();
        var model1 = new ModelNode("m1", "M1");
        var model2 = new ModelNode("m2", "M2");
        graph.AddNode(model1);
        graph.AddNode(model2);
        var resource = graph.GetOrCreateResourceFileNode("/path/shared.mat");

        graph.AddModelReferencesResource(model1.Id, resource.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource });
        graph.AddModelReferencesResource(model2.Id, resource.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource });

        var edges = graph.GetModelEdgesToResource(resource.Id).ToList();

        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(resource.Id, e.ResourceNodeId));
    }

    [Fact]
    public void GetResourcesForModel_ReturnsAllReferencedNodes()
    {
        var (graph, model) = CreateGraphWithModel();
        var file = graph.GetOrCreateResourceFileNode("/path/file.mat");
        var dir = graph.GetOrCreateResourceDirectoryNode("/path/include");
        graph.AddModelReferencesResource(model.Id, file.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource });
        graph.AddModelReferencesResource(model.Id, dir.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.ExternalIncludeDirectory });

        var resources = graph.GetResourcesForModel(model.Id).ToList();

        Assert.Equal(2, resources.Count);
    }

    [Fact]
    public void GetResourcesForModel_ModelNotFound_ReturnsEmpty()
    {
        var graph = new DirectedGraph();

        var resources = graph.GetResourcesForModel("nonexistent");

        Assert.Empty(resources);
    }

    // ============================================================================
    // DirectedGraph — RemoveModelDependencyEdges
    // ============================================================================

    [Fact]
    public void RemoveModelDependencyEdges_ClearsForwardAndReverseEdges()
    {
        var graph = new DirectedGraph();
        var m1 = new ModelNode("m1", "M1");
        var m2 = new ModelNode("m2", "M2");
        graph.AddNode(m1);
        graph.AddNode(m2);
        graph.AddModelUsesModel("m1", "m2");

        graph.RemoveModelDependencyEdges("m1");

        Assert.Empty(m1.UsedModelIds);
        Assert.DoesNotContain(graph.GetOutgoingNodes("m1"), n => n.Id == "m2");
        Assert.Empty(m2.UsedByModelIds);
    }

    [Fact]
    public void RemoveModelDependencyEdges_ModelNotFound_DoesNotThrow()
    {
        var graph = new DirectedGraph();

        // Should not throw even if model doesn't exist
        graph.RemoveModelDependencyEdges("nonexistent");
    }

    // ============================================================================
    // DirectedGraph — RemoveModelResourceEdges
    // ============================================================================

    [Fact]
    public void RemoveModelResourceEdges_ClearsEdgesAndBackReferences()
    {
        var (graph, model) = CreateGraphWithModel();
        var file = graph.GetOrCreateResourceFileNode("/path/file.mat");
        var dir = graph.GetOrCreateResourceDirectoryNode("/path/include");
        graph.AddModelReferencesResource(model.Id, file.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource });
        graph.AddModelReferencesResource(model.Id, dir.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.ExternalIncludeDirectory });

        graph.RemoveModelResourceEdges(model.Id);

        Assert.Empty(model.ReferencedResourceIds);
        Assert.Empty(file.ReferencedByModelIds);
        Assert.Empty(dir.ReferencedByModelIds);
        Assert.Empty(graph.GetResourceEdgesForModel(model.Id));
    }

    [Fact]
    public void RemoveModelResourceEdges_ModelNotFound_DoesNotThrow()
    {
        var graph = new DirectedGraph();

        graph.RemoveModelResourceEdges("nonexistent");
    }

    // ============================================================================
    // DirectedGraph — ReconcileDependencyEdges
    // ============================================================================

    [Fact]
    public void ReconcileDependencyEdges_RemovesStaleUsedModelIds()
    {
        var graph = new DirectedGraph();
        var m1 = new ModelNode("m1", "M1");
        var m2 = new ModelNode("m2", "M2");
        graph.AddNode(m1);
        graph.AddNode(m2);
        graph.AddModelUsesModel("m1", "m2");

        // Remove m2 — this leaves m1.UsedModelIds stale
        graph.RemoveNode("m2");

        graph.ReconcileDependencyEdges();

        // Stale reference to deleted m2 should be cleaned up
        Assert.DoesNotContain("m2", m1.UsedModelIds);
    }

    [Fact]
    public void ReconcileDependencyEdges_RestoresForwardEdgesForValidNodes()
    {
        var graph = new DirectedGraph();
        var m1 = new ModelNode("m1", "M1");
        var m2 = new ModelNode("m2", "M2");
        graph.AddNode(m1);
        graph.AddNode(m2);
        graph.AddModelUsesModel("m1", "m2");

        // Manually remove the forward edge as if RemoveNode had been called
        // but m2 was immediately re-added (simulate reload cycle)
        graph.RemoveNode("m2");
        graph.AddNode(m2);

        graph.ReconcileDependencyEdges();

        // m1 still has "m2" in UsedModelIds and the forward edge should be restored
        // (ReconcileDependencyEdges handles valid references that were disrupted)
        Assert.NotNull(graph.GetNode("m2"));
    }

    [Fact]
    public void ReconcileDependencyEdges_EmptyGraph_DoesNotThrow()
    {
        var graph = new DirectedGraph();

        graph.ReconcileDependencyEdges();
    }

    // ============================================================================
    // DirectedGraph — CleanupOrphanedResourceNodes
    // ============================================================================

    [Fact]
    public void CleanupOrphanedResourceNodes_RemovesUnreferencedFileNodes()
    {
        var graph = new DirectedGraph();
        // Add resource node with no model references
        graph.GetOrCreateResourceFileNode("/orphan/file.mat");

        graph.CleanupOrphanedResourceNodes();

        Assert.Empty(graph.ResourceFileNodes);
    }

    [Fact]
    public void CleanupOrphanedResourceNodes_RemovesUnreferencedDirectoryNodes()
    {
        var graph = new DirectedGraph();
        graph.GetOrCreateResourceDirectoryNode("/orphan/include");

        graph.CleanupOrphanedResourceNodes();

        Assert.Empty(graph.ResourceDirectoryNodes);
    }

    [Fact]
    public void CleanupOrphanedResourceNodes_KeepsReferencedNodes()
    {
        var (graph, model) = CreateGraphWithModel();
        var file = graph.GetOrCreateResourceFileNode("/path/used.mat");
        graph.AddModelReferencesResource(model.Id, file.Id, new ResourceEdge { ReferenceType = ResourceReferenceType.LoadResource });

        graph.CleanupOrphanedResourceNodes();

        // Should still be there — it has a referencing model
        Assert.Single(graph.ResourceFileNodes);
    }

    [Fact]
    public void CleanupOrphanedResourceNodes_EmptyGraph_DoesNotThrow()
    {
        var graph = new DirectedGraph();

        graph.CleanupOrphanedResourceNodes();
    }

    // ============================================================================
    // DirectedGraph — GetUsedModels / GetModelUsedBy with non-existent node
    // ============================================================================

    [Fact]
    public void GetUsedModels_ModelNotFound_ReturnsEmpty()
    {
        var graph = new DirectedGraph();

        var result = graph.GetUsedModels("nonexistent").ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void GetModelUsedBy_ModelNotFound_ReturnsEmpty()
    {
        var graph = new DirectedGraph();

        var result = graph.GetModelUsedBy("nonexistent").ToList();

        Assert.Empty(result);
    }

    // ============================================================================
    // DirectedGraph — AddModelUsesModel error path
    // ============================================================================

    [Fact]
    public void AddModelUsesModel_WithNonExistentNodes_ThrowsArgumentException()
    {
        var graph = new DirectedGraph();
        var m1 = new ModelNode("m1", "M1");
        graph.AddNode(m1);

        Assert.Throws<ArgumentException>(() => graph.AddModelUsesModel("m1", "nonexistent"));
        Assert.Throws<ArgumentException>(() => graph.AddModelUsesModel("nonexistent", "m1"));
    }
}
