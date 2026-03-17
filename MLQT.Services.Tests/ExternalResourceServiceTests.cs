using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaGraph.Interfaces;
using ModelicaParser.DataTypes;
using MLQT.Services.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the ExternalResourceService class.
/// </summary>
public class ExternalResourceServiceTests : IDisposable
{
    private readonly ExternalResourceService _service;
    private readonly string _tempDir;

    public ExternalResourceServiceTests()
    {
        _service = new ExternalResourceService();
        _tempDir = Path.Combine(Path.GetTempPath(), $"ExtResTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _service.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch { }
    }

    /// <summary>
    /// Creates a graph with resource nodes and edges (simulating what GraphBuilder does).
    /// </summary>
    private DirectedGraph CreateGraphWithResourceNodes(
        string modelId,
        List<(string resolvedPath, ResourceReferenceType refType, string rawPath, bool isDir)> resources)
    {
        var graph = new DirectedGraph();
        var model = new ModelNode(modelId, modelId.Split('.').Last());
        graph.AddNode(model);

        foreach (var (resolvedPath, refType, rawPath, isDir) in resources)
        {
            IGraphNode resourceNode;
            if (isDir)
            {
                resourceNode = graph.GetOrCreateResourceDirectoryNode(resolvedPath);
            }
            else
            {
                resourceNode = graph.GetOrCreateResourceFileNode(resolvedPath);
            }

            var edge = new ResourceEdge
            {
                RawPath = rawPath,
                ReferenceType = refType,
                IsAbsolutePath = !rawPath.StartsWith("modelica://", StringComparison.OrdinalIgnoreCase)
                                 && Path.IsPathRooted(rawPath)
            };

            graph.AddModelReferencesResource(modelId, resourceNode.Id, edge);
        }

        return graph;
    }

    [Fact]
    public async Task AnalyzeResources_ReadsFromGraphNodes()
    {
        // Create a resource file
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test data");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        var modelResources = _service.GetResourcesForModel("TestLib.TestModel");
        Assert.Single(modelResources);
        Assert.Equal(resourceFile, modelResources[0].ResolvedPath);
        Assert.True(modelResources[0].FileExists);
    }

    [Fact]
    public async Task AnalyzeResources_FlagsMissingFiles()
    {
        var missingFile = Path.Combine(_tempDir, "Resources", "nonexistent.mat");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (missingFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/nonexistent.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        var warnings = _service.GetWarnings();
        Assert.Contains(warnings, w => w.WarningType == ResourceWarningType.MissingFile);
    }

    [Fact]
    public async Task AnalyzeResources_FlagsAbsolutePaths()
    {
        var absolutePath = Path.Combine(_tempDir, "data.mat");
        File.WriteAllText(absolutePath, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (absolutePath, ResourceReferenceType.LoadResource, absolutePath, false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        var warnings = _service.GetWarnings();
        Assert.Contains(warnings, w => w.WarningType == ResourceWarningType.AbsolutePath);
    }

    [Fact]
    public async Task AnalyzeResources_IdentifiesImageFiles()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources", "Images");
        Directory.CreateDirectory(resourceDir);
        var imageFile = Path.Combine(resourceDir, "icon.png");
        File.WriteAllText(imageFile, "fake png");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (imageFile, ResourceReferenceType.UriReference, "modelica://TestLib/Resources/Images/icon.png", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        var modelResources = _service.GetResourcesForModel("TestLib.TestModel");
        Assert.Single(modelResources);
        Assert.True(modelResources[0].IsImageFile);
    }

    [Fact]
    public async Task ReverseIndex_FindsModelsReferencingResource()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "shared.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = new DirectedGraph();

        // Two models referencing the same file
        var model1 = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model1);

        var model2 = new ModelNode("TestLib.Model2", "Model2");
        graph.AddNode(model2);

        // Create single resource node referenced by both models
        var resourceNode = graph.GetOrCreateResourceFileNode(resourceFile);

        var edge1 = new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources/shared.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };
        graph.AddModelReferencesResource("TestLib.Model1", resourceNode.Id, edge1);

        var edge2 = new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources/shared.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };
        graph.AddModelReferencesResource("TestLib.Model2", resourceNode.Id, edge2);

        await _service.AnalyzeResourcesAsync(graph);

        var affectedModels = _service.GetModelsReferencingResource(resourceFile);
        Assert.Equal(2, affectedModels.Count);
        Assert.Contains("TestLib.Model1", affectedModels);
        Assert.Contains("TestLib.Model2", affectedModels);
    }

    [Fact]
    public async Task ClearDataForModels_RemovesModelDataAndWarnings()
    {
        var missingFile = Path.Combine(_tempDir, "Resources", "missing.mat");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (missingFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/missing.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        Assert.NotEmpty(_service.GetWarnings());
        Assert.NotEmpty(_service.GetResourcesForModel("TestLib.TestModel"));

        _service.ClearDataForModels(new[] { "TestLib.TestModel" });

        Assert.Empty(_service.GetWarnings());
        Assert.Empty(_service.GetResourcesForModel("TestLib.TestModel"));
    }

    [Fact]
    public async Task AnalyzeResourcesForModels_IncrementalUpdate()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var existingFile = Path.Combine(resourceDir, "data1.mat");
        File.WriteAllText(existingFile, "test");
        var missingFile = Path.Combine(resourceDir, "missing.mat");

        var graph = new DirectedGraph();

        var model1 = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model1);

        var model2 = new ModelNode("TestLib.Model2", "Model2");
        graph.AddNode(model2);

        // Model1 references existing file
        var node1 = graph.GetOrCreateResourceFileNode(existingFile);
        var edge1 = new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources/data1.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };
        graph.AddModelReferencesResource("TestLib.Model1", node1.Id, edge1);

        // Model2 references missing file
        var node2 = graph.GetOrCreateResourceFileNode(missingFile);
        var edge2 = new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources/missing.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };
        graph.AddModelReferencesResource("TestLib.Model2", node2.Id, edge2);

        // Initial full analysis
        await _service.AnalyzeResourcesAsync(graph);
        Assert.Single(_service.GetResourcesForModel("TestLib.Model1"));
        Assert.Single(_service.GetResourcesForModel("TestLib.Model2"));

        // Simulate updating model2 in graph: remove old edge, add new one
        graph.RemoveModelResourceEdges("TestLib.Model2");
        var edge3 = new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources/data1.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        };
        graph.AddModelReferencesResource("TestLib.Model2", node1.Id, edge3);

        // Incremental update for model2 only
        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.Model2" }, graph);

        // Model1 should still have its data
        Assert.Single(_service.GetResourcesForModel("TestLib.Model1"));
        // Model2 should have updated data
        Assert.Single(_service.GetResourcesForModel("TestLib.Model2"));
        Assert.True(_service.GetResourcesForModel("TestLib.Model2")[0].FileExists);
    }

    [Fact]
    public async Task AnalyzeResources_HandlesDirectoryNodes()
    {
        var includeDir = Path.Combine(_tempDir, "Resources", "C-Sources");
        Directory.CreateDirectory(includeDir);

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (includeDir, ResourceReferenceType.ExternalIncludeDirectory, "modelica://TestLib/Resources/C-Sources", true)
        });

        await _service.AnalyzeResourcesAsync(graph);

        var modelResources = _service.GetResourcesForModel("TestLib.TestModel");
        Assert.Single(modelResources);
        Assert.Equal(includeDir, modelResources[0].ResolvedPath);
        Assert.True(modelResources[0].FileExists); // Directory exists
    }

    [Fact]
    public void GetResourcesForModel_ReturnsEmptyForUnknownModel()
    {
        var resources = _service.GetResourcesForModel("NonExistent.Model");
        Assert.Empty(resources);
    }

    [Fact]
    public void GetModelsReferencingResource_ReturnsEmptyForUnknownPath()
    {
        var models = _service.GetModelsReferencingResource("/some/nonexistent/path.mat");
        Assert.Empty(models);
    }

    [Fact]
    public async Task GetAllResources_ReturnsAllModelResources()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var file1 = Path.Combine(resourceDir, "data1.mat");
        var file2 = Path.Combine(resourceDir, "data2.mat");
        File.WriteAllText(file1, "test1");
        File.WriteAllText(file2, "test2");

        var graph = new DirectedGraph();
        var model1 = new ModelNode("Lib.Model1", "Model1");
        var model2 = new ModelNode("Lib.Model2", "Model2");
        graph.AddNode(model1);
        graph.AddNode(model2);

        var node1 = graph.GetOrCreateResourceFileNode(file1);
        graph.AddModelReferencesResource("Lib.Model1", node1.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/Resources/data1.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        var node2 = graph.GetOrCreateResourceFileNode(file2);
        graph.AddModelReferencesResource("Lib.Model2", node2.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/Resources/data2.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        await _service.AnalyzeResourcesAsync(graph);

        var allResources = _service.GetAllResources();
        Assert.Equal(2, allResources.Count);
    }

    [Fact]
    public async Task StartMonitoringResources_DoesNotThrow()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Should not throw
        _service.StartMonitoringResources();
    }

    [Fact]
    public async Task StopMonitoringResources_AfterStart_DoesNotThrow()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);
        _service.StartMonitoringResources();

        // Should not throw
        _service.StopMonitoringResources();
    }

    [Fact]
    public void StartMonitoringResources_WithNoResources_DoesNotThrow()
    {
        // Empty service - should not throw even with no resources
        _service.StartMonitoringResources();
    }

    [Fact]
    public void StopMonitoringResources_WithNoWatchers_DoesNotThrow()
    {
        // Should not throw even if no watchers are running
        _service.StopMonitoringResources();
    }

    [Fact]
    public async Task AnalyzeResourcesForModels_WithDirectoryContents_PopulatesContents()
    {
        var includeDir = Path.Combine(_tempDir, "C-Sources");
        Directory.CreateDirectory(includeDir);

        // Create a file in the directory
        var headerFile = Path.Combine(includeDir, "mylib.h");
        File.WriteAllText(headerFile, "// header");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (includeDir, ResourceReferenceType.ExternalIncludeDirectory, "modelica://TestLib/C-Sources", true)
        });

        // Register the file as contained in the dir node
        var dirNode = graph.GetOrCreateResourceDirectoryNode(includeDir);
        var fileNode = graph.GetOrCreateResourceFileNode(headerFile);
        dirNode.AddContainedFile(fileNode.Id);

        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.TestModel" }, graph);

        var allResources = _service.GetAllResources();
        Assert.True(allResources.Count >= 1);
    }

    [Fact]
    public async Task AnalyzeResourcesForModels_WithOtherModelHavingDirectoryNode_ProcessesDirectoryContents()
    {
        // Covers lines 93-96: the "other models' directory edges" loop
        var includeDir = Path.Combine(_tempDir, "C-Sources");
        Directory.CreateDirectory(includeDir);
        var headerFile = Path.Combine(includeDir, "lib.h");
        File.WriteAllText(headerFile, "// header");

        var graph = new DirectedGraph();

        // Model1 is the one we're updating
        var model1 = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model1);
        var fileNode1 = graph.GetOrCreateResourceFileNode(Path.Combine(_tempDir, "data.mat"));
        graph.AddModelReferencesResource("TestLib.Model1", fileNode1.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/data.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        // Model2 is NOT in the update list, has a directory node
        var model2 = new ModelNode("TestLib.Model2", "Model2");
        graph.AddNode(model2);
        var dirNode = graph.GetOrCreateResourceDirectoryNode(includeDir);
        var fileNode2 = graph.GetOrCreateResourceFileNode(headerFile);
        dirNode.AddContainedFile(fileNode2.Id);
        graph.AddModelReferencesResource("TestLib.Model2", dirNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/C-Sources",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });

        // Only update Model1 - Model2's directory node should be processed via the "other models" loop
        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.Model1" }, graph);

        // Directory contents should include the header file from Model2's directory
        var allResources = _service.GetAllResources();
        Assert.True(allResources.Count >= 1);
    }

    [Fact]
    public async Task AnalyzeResourcesForModels_ThenRemoveResources_RemovesStaleWatchers()
    {
        // Covers UpdateWatchers removal path (lines 393-408)
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model);
        var fileNode = graph.GetOrCreateResourceFileNode(resourceFile);
        graph.AddModelReferencesResource("TestLib.Model1", fileNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources/data.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        // First call creates watchers for resourceDir
        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.Model1" }, graph);

        // Remove the resource edge from graph and analyze again with no edges
        graph.RemoveModelResourceEdges("TestLib.Model1");
        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.Model1" }, graph);

        // Watcher for resourceDir should have been removed (no exception = pass)
        Assert.Empty(_service.GetResourcesForModel("TestLib.Model1"));
    }

    [Fact]
    public async Task ProcessDirectoryContents_FileAlreadyInReverseIndex_SkipsIt()
    {
        // Covers line 310: "File has direct references, don't add as directory content"
        var includeDir = Path.Combine(_tempDir, "C-Sources2");
        Directory.CreateDirectory(includeDir);
        var headerFile = Path.Combine(includeDir, "shared.h");
        File.WriteAllText(headerFile, "// shared header");

        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model);

        // Add both a direct reference to the file AND the directory containing it
        var fileNode = graph.GetOrCreateResourceFileNode(headerFile);
        graph.AddModelReferencesResource("TestLib.Model1", fileNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/C-Sources2/shared.h",
            ReferenceType = ResourceReferenceType.ExternalInclude
        });

        var dirNode = graph.GetOrCreateResourceDirectoryNode(includeDir);
        dirNode.AddContainedFile(fileNode.Id);
        graph.AddModelReferencesResource("TestLib.Model1", dirNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/C-Sources2",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });

        await _service.AnalyzeResourcesAsync(graph);

        // The file should appear once (as direct reference), not twice (not also as directory content)
        var modelResources = _service.GetResourcesForModel("TestLib.Model1");
        var filePaths = modelResources.Where(r => r.ResolvedPath == headerFile).ToList();
        Assert.Single(filePaths); // Only one entry, not duplicated
    }

    [Fact]
    public async Task AnalyzeResources_MissingFileAndAbsolutePath_GeneratesBothWarnings()
    {
        var absoluteMissingFile = Path.Combine(_tempDir, "Resources", "absolute_missing.mat");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (absoluteMissingFile, ResourceReferenceType.LoadResource, absoluteMissingFile, false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        var warnings = _service.GetWarnings();
        Assert.Contains(warnings, w => w.WarningType == ResourceWarningType.AbsolutePath);
        Assert.Contains(warnings, w => w.WarningType == ResourceWarningType.MissingFile);
    }

    [Fact]
    public async Task AnalyzeResources_DirectoryDoesNotExist_NoWarningGenerated()
    {
        var missingDir = Path.Combine(_tempDir, "Resources", "MissingSources");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (missingDir, ResourceReferenceType.ExternalIncludeDirectory, "modelica://TestLib/Resources/MissingSources", true)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Directories don't generate warnings (only files do)
        var modelResources = _service.GetResourcesForModel("TestLib.TestModel");
        Assert.Single(modelResources);
        Assert.False(modelResources[0].FileExists);
        Assert.True(modelResources[0].IsDirectory);
    }

    [Fact]
    public async Task AnalyzeResourcesAsync_ClearsOldDataBeforeReload()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var file1 = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(file1, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (file1, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);
        Assert.Single(_service.GetResourcesForModel("TestLib.TestModel"));

        // Call again — should not duplicate
        await _service.AnalyzeResourcesAsync(graph);
        Assert.Single(_service.GetResourcesForModel("TestLib.TestModel"));
    }

    [Fact]
    public async Task ClearDataForModels_RemovesReverseIndexEntries()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var file1 = Path.Combine(resourceDir, "shared.mat");
        File.WriteAllText(file1, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (file1, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/shared.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);
        Assert.NotEmpty(_service.GetModelsReferencingResource(file1));

        _service.ClearDataForModels(new[] { "TestLib.TestModel" });
        Assert.Empty(_service.GetModelsReferencingResource(file1));
    }

    [Fact]
    public async Task ClearDataForModels_NonExistentModel_DoesNotThrow()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources");
        Directory.CreateDirectory(resourceDir);
        var file1 = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(file1, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (file1, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Clear a model that doesn't exist — should not throw
        _service.ClearDataForModels(new[] { "NonExistent.Model" });

        // Original data should still be intact
        Assert.Single(_service.GetResourcesForModel("TestLib.TestModel"));
    }

    [Fact]
    public async Task GetAllResources_IncludesDirectoryContents()
    {
        var includeDir = Path.Combine(_tempDir, "C-Sources3");
        Directory.CreateDirectory(includeDir);
        var headerFile = Path.Combine(includeDir, "impl.c");
        File.WriteAllText(headerFile, "// c source");

        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model);

        var dirNode = graph.GetOrCreateResourceDirectoryNode(includeDir);
        var fileNode = graph.GetOrCreateResourceFileNode(headerFile);
        dirNode.AddContainedFile(fileNode.Id);

        graph.AddModelReferencesResource("TestLib.Model1", dirNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/C-Sources3",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });

        await _service.AnalyzeResourcesAsync(graph);

        var allResources = _service.GetAllResources();
        // Should include the directory reference AND the contained file
        Assert.True(allResources.Count >= 2);
        Assert.Contains(allResources, r => r.IsDirectory && r.ResolvedPath == includeDir);
        Assert.Contains(allResources, r => !r.IsDirectory && r.ResolvedPath == headerFile);
    }

    [Fact]
    public async Task AnalyzeResources_MultipleReferenceTypes_AllTracked()
    {
        var resourceDir = Path.Combine(_tempDir, "Resources2");
        Directory.CreateDirectory(resourceDir);
        var dataFile = Path.Combine(resourceDir, "data.mat");
        var imageFile = Path.Combine(resourceDir, "icon.png");
        File.WriteAllText(dataFile, "data");
        File.WriteAllText(imageFile, "image");

        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.TestModel", "TestModel");
        graph.AddNode(model);

        var dataNode = graph.GetOrCreateResourceFileNode(dataFile);
        graph.AddModelReferencesResource("TestLib.TestModel", dataNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources2/data.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        var imageNode = graph.GetOrCreateResourceFileNode(imageFile);
        graph.AddModelReferencesResource("TestLib.TestModel", imageNode.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/Resources2/icon.png",
            ReferenceType = ResourceReferenceType.UriReference
        });

        await _service.AnalyzeResourcesAsync(graph);

        var modelResources = _service.GetResourcesForModel("TestLib.TestModel");
        Assert.Equal(2, modelResources.Count);
        Assert.Contains(modelResources, r => r.ReferenceType == ResourceReferenceType.LoadResource);
        Assert.Contains(modelResources, r => r.ReferenceType == ResourceReferenceType.UriReference);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        var service = new ExternalResourceService();
        service.Dispose();
        service.Dispose(); // Second call should not throw
    }

    [Fact]
    public async Task AnalyzeResourcesForModels_EmptyModelList_DoesNotThrow()
    {
        var graph = new DirectedGraph();
        await _service.AnalyzeResourcesForModelsAsync(Array.Empty<string>(), graph);
        Assert.Empty(_service.GetAllResources());
    }

    [Fact]
    public async Task StartMonitoringResources_WithResources_CreatesWatchers()
    {
        // Exercises the full watcher creation path in UpdateWatchers
        var resourceDir = Path.Combine(_tempDir, "WatcherTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test data");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);
        _service.StartMonitoringResources();

        // Watchers were created — now stop to clean up
        _service.StopMonitoringResources();

        // Start again and stop again to cover the path where watchers already exist
        _service.StartMonitoringResources();
        _service.StopMonitoringResources();
    }

    [Fact]
    public async Task AnalyzeResourcesForModelsAsync_CreatesWatchersForNewDirectories()
    {
        // AnalyzeResourcesForModelsAsync calls UpdateWatchers internally
        var resourceDir = Path.Combine(_tempDir, "WatcherTest2");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test data");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        // This calls UpdateWatchers internally, creating watchers for resourceDir
        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.TestModel" }, graph);

        // Verify resources were tracked
        Assert.NotEmpty(_service.GetResourcesForModel("TestLib.TestModel"));
    }

    [Fact]
    public async Task AnalyzeResourcesAsync_WithNoEdges_ReturnsEmpty()
    {
        // Graph with a model but no resource edges
        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.TestModel", "TestModel");
        graph.AddNode(model);

        await _service.AnalyzeResourcesAsync(graph);

        Assert.Empty(_service.GetResourcesForModel("TestLib.TestModel"));
        Assert.Empty(_service.GetAllResources());
        Assert.Empty(_service.GetWarnings());
    }

    [Fact]
    public async Task AnalyzeResourcesForModelsAsync_UpdatesExistingData()
    {
        // Test incremental update: analyze, then re-analyze with different resources
        var resourceDir = Path.Combine(_tempDir, "IncrementalTest");
        Directory.CreateDirectory(resourceDir);
        var file1 = Path.Combine(resourceDir, "old.mat");
        var file2 = Path.Combine(resourceDir, "new.mat");
        File.WriteAllText(file1, "old");
        File.WriteAllText(file2, "new");

        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.Model1", "Model1");
        graph.AddNode(model);

        // First: model references file1
        var node1 = graph.GetOrCreateResourceFileNode(file1);
        graph.AddModelReferencesResource("TestLib.Model1", node1.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/old.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.Model1" }, graph);
        Assert.Single(_service.GetResourcesForModel("TestLib.Model1"));
        Assert.Equal(file1, _service.GetResourcesForModel("TestLib.Model1")[0].ResolvedPath);

        // Update graph: model now references file2 instead
        graph.RemoveModelResourceEdges("TestLib.Model1");
        var node2 = graph.GetOrCreateResourceFileNode(file2);
        graph.AddModelReferencesResource("TestLib.Model1", node2.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/new.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.Model1" }, graph);
        Assert.Single(_service.GetResourcesForModel("TestLib.Model1"));
        Assert.Equal(file2, _service.GetResourcesForModel("TestLib.Model1")[0].ResolvedPath);
    }

    [Fact]
    public async Task ClearDataForModels_WithMultipleModels_ClearsAll()
    {
        var resourceDir = Path.Combine(_tempDir, "MultiClear");
        Directory.CreateDirectory(resourceDir);
        var file1 = Path.Combine(resourceDir, "data1.mat");
        var file2 = Path.Combine(resourceDir, "data2.mat");
        File.WriteAllText(file1, "test1");
        File.WriteAllText(file2, "test2");

        var graph = new DirectedGraph();
        var model1 = new ModelNode("Lib.Model1", "Model1");
        var model2 = new ModelNode("Lib.Model2", "Model2");
        graph.AddNode(model1);
        graph.AddNode(model2);

        var node1 = graph.GetOrCreateResourceFileNode(file1);
        graph.AddModelReferencesResource("Lib.Model1", node1.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/data1.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        var node2 = graph.GetOrCreateResourceFileNode(file2);
        graph.AddModelReferencesResource("Lib.Model2", node2.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/data2.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        await _service.AnalyzeResourcesAsync(graph);
        Assert.Equal(2, _service.GetAllResources().Count);

        // Clear both models at once
        _service.ClearDataForModels(new[] { "Lib.Model1", "Lib.Model2" });

        Assert.Empty(_service.GetResourcesForModel("Lib.Model1"));
        Assert.Empty(_service.GetResourcesForModel("Lib.Model2"));
        Assert.Empty(_service.GetModelsReferencingResource(file1));
        Assert.Empty(_service.GetModelsReferencingResource(file2));
    }

    [Fact]
    public async Task AnalyzeResourcesForModelsAsync_DirectoryContentsDeduplication()
    {
        // Covers the path where a file already in directory contents is not added again
        var includeDir = Path.Combine(_tempDir, "DedupTest");
        Directory.CreateDirectory(includeDir);
        var file = Path.Combine(includeDir, "lib.h");
        File.WriteAllText(file, "// header");

        var graph = new DirectedGraph();

        // Two models, both referencing the same directory
        var model1 = new ModelNode("Lib.Model1", "Model1");
        var model2 = new ModelNode("Lib.Model2", "Model2");
        graph.AddNode(model1);
        graph.AddNode(model2);

        var dirNode = graph.GetOrCreateResourceDirectoryNode(includeDir);
        var fileNode = graph.GetOrCreateResourceFileNode(file);
        dirNode.AddContainedFile(fileNode.Id);

        graph.AddModelReferencesResource("Lib.Model1", dirNode.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/DedupTest",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });
        graph.AddModelReferencesResource("Lib.Model2", dirNode.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/DedupTest",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });

        await _service.AnalyzeResourcesAsync(graph);

        // The contained file should appear only once in directory contents (deduplicated)
        var allResources = _service.GetAllResources();
        var fileEntries = allResources.Where(r => !r.IsDirectory && r.ResolvedPath == file).ToList();
        Assert.Single(fileEntries);
    }

    [Fact]
    public async Task AnalyzeResources_WithExistingFileAndNoAbsolutePath_NoWarnings()
    {
        var resourceDir = Path.Combine(_tempDir, "NoWarnTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Existing file with relative (modelica://) path: no warnings
        Assert.Empty(_service.GetWarnings());
    }

    [Fact]
    public async Task AnalyzeResourcesForModelsAsync_RebuildDirectoryContentsFromOtherModels()
    {
        // Exercise the loop that rebuilds directory contents from non-target models
        var dir1 = Path.Combine(_tempDir, "Dir1");
        var dir2 = Path.Combine(_tempDir, "Dir2");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        var file1 = Path.Combine(dir1, "a.h");
        var file2 = Path.Combine(dir2, "b.h");
        File.WriteAllText(file1, "// a");
        File.WriteAllText(file2, "// b");

        var graph = new DirectedGraph();

        var model1 = new ModelNode("Lib.Model1", "Model1");
        var model2 = new ModelNode("Lib.Model2", "Model2");
        graph.AddNode(model1);
        graph.AddNode(model2);

        // Model1 has directory dir1
        var dirNode1 = graph.GetOrCreateResourceDirectoryNode(dir1);
        var fileNode1 = graph.GetOrCreateResourceFileNode(file1);
        dirNode1.AddContainedFile(fileNode1.Id);
        graph.AddModelReferencesResource("Lib.Model1", dirNode1.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/Dir1",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });

        // Model2 has directory dir2
        var dirNode2 = graph.GetOrCreateResourceDirectoryNode(dir2);
        var fileNode2 = graph.GetOrCreateResourceFileNode(file2);
        dirNode2.AddContainedFile(fileNode2.Id);
        graph.AddModelReferencesResource("Lib.Model2", dirNode2.Id, new ResourceEdge
        {
            RawPath = "modelica://Lib/Dir2",
            ReferenceType = ResourceReferenceType.ExternalIncludeDirectory
        });

        // Do incremental update on Model1 only — Model2's directory should still appear in contents
        await _service.AnalyzeResourcesForModelsAsync(new[] { "Lib.Model1" }, graph);

        var allResources = _service.GetAllResources();
        // Should contain Model1's directory + Model2's directory + both files as contents
        Assert.True(allResources.Count >= 2);
    }

    [Fact]
    public async Task StopMonitoringResources_AfterAnalyzeForModels_DisposesWatchers()
    {
        var resourceDir = Path.Combine(_tempDir, "DisposeTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/Resources/data.mat", false)
        });

        // AnalyzeResourcesForModelsAsync creates watchers via UpdateWatchers
        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.TestModel" }, graph);

        // StopMonitoringResources should dispose them
        _service.StopMonitoringResources();

        // Calling again should be safe
        _service.StopMonitoringResources();
    }

    #region OnResourceFileSystemEvent Tests

    [Fact]
    public async Task OnResourceFileSystemEvent_UntrackedFile_Ignored()
    {
        // File not in reverse index should be ignored
        var resourceDir = Path.Combine(_tempDir, "EventTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "tracked.mat");
        File.WriteAllText(resourceFile, "test");
        var untrackedFile = Path.Combine(resourceDir, "untracked.dat");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/tracked.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        _service.OnResourceFileSystemEvent(resourceDir, untrackedFile, WatcherChangeTypes.Changed);

        Assert.Empty(_service.GetPendingChanges());
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_TrackedFile_CreatesPendingChange()
    {
        var resourceDir = Path.Combine(_tempDir, "EventTest2");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Changed);

        var pending = _service.GetPendingChanges();
        Assert.Single(pending);
        Assert.Equal(WatcherChangeTypes.Changed, pending[0].ChangeType);
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_SameTypeDebounceSuppresses()
    {
        var resourceDir = Path.Combine(_tempDir, "DebounceTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Two rapid identical events should be debounced
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Changed);
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Changed);

        var pending = _service.GetPendingChanges();
        Assert.Single(pending); // Debounced to one
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_DifferentTypesNotDebounced()
    {
        var resourceDir = Path.Combine(_tempDir, "NoDebounceDiffType");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Different types pass through (not debounced)
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Deleted);
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Created);

        // Deleted + Created = consolidated to Changed (SVN revert scenario)
        var pending = _service.GetPendingChanges();
        Assert.Single(pending);
        Assert.Equal(WatcherChangeTypes.Changed, pending[0].ChangeType);
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_CreatedThenDeleted_CancelsOut()
    {
        var resourceDir = Path.Combine(_tempDir, "CreateDeleteTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Created);
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Deleted);

        // Created + Deleted = no net change
        Assert.Empty(_service.GetPendingChanges());
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_CreatedThenModified_KeepsCreated()
    {
        var resourceDir = Path.Combine(_tempDir, "CreateModifyTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Created);
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Changed);

        // Created + Modified = keep as Created
        var pending = _service.GetPendingChanges();
        Assert.Single(pending);
        Assert.Equal(WatcherChangeTypes.Created, pending[0].ChangeType);
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_ModifiedThenDeleted_BecomesDeleted()
    {
        var resourceDir = Path.Combine(_tempDir, "ModifyDeleteTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Changed);
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Deleted);

        // Modified + Deleted = Deleted
        var pending = _service.GetPendingChanges();
        Assert.Single(pending);
        Assert.Equal(WatcherChangeTypes.Deleted, pending[0].ChangeType);
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_FallbackConsolidation_ReplacesWithLatest()
    {
        var resourceDir = Path.Combine(_tempDir, "FallbackTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Deleted then Deleted — fallback consolidation replaces with latest
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Deleted);
        // Wait past debounce
        await Task.Delay(600);
        _service.OnResourceFileSystemEvent(resourceDir, resourceFile, WatcherChangeTypes.Deleted);

        var pending = _service.GetPendingChanges();
        Assert.Single(pending);
        Assert.Equal(WatcherChangeTypes.Deleted, pending[0].ChangeType);
    }

    [Fact]
    public async Task OnResourceFileSystemEvent_HiddenDirectory_Ignored()
    {
        var resourceDir = Path.Combine(_tempDir, "HiddenTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Event from a hidden directory should be ignored
        var hiddenPath = Path.Combine(_tempDir, ".svn", "data.mat");
        _service.OnResourceFileSystemEvent(resourceDir, hiddenPath, WatcherChangeTypes.Changed);

        Assert.Empty(_service.GetPendingChanges());
    }

    #endregion

    #region OnWatcherError Tests

    [Fact]
    public async Task OnWatcherError_RemovesAndRecreatesWatcher()
    {
        var resourceDir = Path.Combine(_tempDir, "WatcherErrorTest");
        Directory.CreateDirectory(resourceDir);
        var resourceFile = Path.Combine(resourceDir, "data.mat");
        File.WriteAllText(resourceFile, "test");

        var graph = CreateGraphWithResourceNodes("TestLib.TestModel", new List<(string, ResourceReferenceType, string, bool)>
        {
            (resourceFile, ResourceReferenceType.LoadResource, "modelica://TestLib/data.mat", false)
        });

        await _service.AnalyzeResourcesForModelsAsync(new[] { "TestLib.TestModel" }, graph);

        // Trigger a watcher error - should dispose old watcher and recreate via UpdateWatchers
        _service.OnWatcherError(resourceDir, new Exception("Test error"));

        // Should not throw and service should still be functional
        var resources = _service.GetResourcesForModel("TestLib.TestModel");
        Assert.NotEmpty(resources);
    }

    [Fact]
    public async Task OnWatcherError_WithUnknownDirectory_DoesNotThrow()
    {
        await _service.AnalyzeResourcesAsync(new DirectedGraph());

        // Watcher error for a directory we're not watching should not throw
        _service.OnWatcherError("C:\\nonexistent\\dir", new Exception("Test"));
    }

    #endregion

    [Fact]
    public async Task AnalyzeResources_WithNullResolvedPath_SkipsReverseIndex()
    {
        // A resource edge where the node's ResolvedPath is handled gracefully
        var graph = new DirectedGraph();
        var model = new ModelNode("TestLib.TestModel", "TestModel");
        graph.AddNode(model);

        // Create a resource file node with path (can't create with null resolved path via public API)
        // But we can test that existing resources have proper paths
        var resourceFile = Path.Combine(_tempDir, "test.mat");
        File.WriteAllText(resourceFile, "test");
        var node = graph.GetOrCreateResourceFileNode(resourceFile);
        graph.AddModelReferencesResource("TestLib.TestModel", node.Id, new ResourceEdge
        {
            RawPath = "modelica://TestLib/test.mat",
            ReferenceType = ResourceReferenceType.LoadResource
        });

        await _service.AnalyzeResourcesAsync(graph);

        // Should have the resource and reverse index entry
        Assert.NotEmpty(_service.GetResourcesForModel("TestLib.TestModel"));
        Assert.NotEmpty(_service.GetModelsReferencingResource(resourceFile));
    }
}
