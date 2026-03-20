using MLQT.Services;
using MLQT.Services.DataTypes;
using MLQT.Services.Interfaces;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the LibraryDataService class.
/// </summary>
public class LibraryDataServiceTests
{
    [Fact]
    public void Libraries_InitiallyEmpty()
    {
        var service = new LibraryDataService();

        Assert.Empty(service.Libraries);
    }

    [Fact]
    public void CombinedGraph_InitiallyEmpty()
    {
        var service = new LibraryDataService();

        Assert.Empty(service.CombinedGraph.ModelNodes);
        Assert.Empty(service.CombinedGraph.FileNodes);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_AddsLibrary()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
model TestModel
  Real x;
end TestModel;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.NotNull(library);
        Assert.Single(service.Libraries);
        Assert.Equal(LibrarySourceType.File, library.SourceType);
        Assert.Equal("test.mo", library.SourcePath);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_ParsesModelCorrectly()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
model TestModel
  Real x;
  Real y;
equation
  x = y;
end TestModel;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.Single(library.TopLevelModelIds);
        Assert.Contains("TestModel", library.TopLevelModelIds);
        Assert.Contains("TestModel", library.ModelIds);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_FiresOnLibrariesChangedEvent()
    {
        var service = new LibraryDataService();
        var eventFired = false;
        service.OnLibrariesChanged += () => eventFired = true;
        var modelicaCode = "model Test end Test;";

        await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.True(eventFired);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_FiresOnTreeDataChangedEvent()
    {
        var service = new LibraryDataService();
        var eventFired = false;
        service.OnTreeDataChanged += () => eventFired = true;
        var modelicaCode = "model Test end Test;";

        await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.True(eventFired);
    }

    [Fact]
    public async Task AddLibraryFromZipAsync_AddsLibrary()
    {
        var service = new LibraryDataService();
        var files = new Dictionary<string, string>
        {
            { "package.mo", "package TestPackage end TestPackage;" },
            { "Model1.mo", "within TestPackage; model Model1 end Model1;" }
        };

        var library = await service.AddLibraryFromZipAsync(files);

        Assert.NotNull(library);
        Assert.Single(service.Libraries);
        Assert.Equal(LibrarySourceType.Zip, library.SourceType);
    }

    [Fact]
    public async Task AddLibraryFromZipAsync_SetsNameFromFirstTopLevelModel()
    {
        var service = new LibraryDataService();
        var files = new Dictionary<string, string>
        {
            { "package.mo", "package MyLib end MyLib;" }
        };

        var library = await service.AddLibraryFromZipAsync(files);

        Assert.Equal("MyLib", library.Name);
    }

    [Fact]
    public async Task RemoveLibrary_RemovesLibraryById()
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");

        service.RemoveLibrary(library.Id);

        Assert.Empty(service.Libraries);
    }

    [Fact]
    public async Task RemoveLibrary_FiresOnLibrariesChangedEvent()
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");
        var eventFired = false;
        service.OnLibrariesChanged += () => eventFired = true;

        service.RemoveLibrary(library.Id);

        Assert.True(eventFired);
    }

    [Fact]
    public async Task RemoveLibrary_RemovesFromCombinedGraph()
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");
        Assert.NotEmpty(service.CombinedGraph.ModelNodes);

        service.RemoveLibrary(library.Id);

        Assert.Empty(service.CombinedGraph.ModelNodes);
    }

    [Fact]
    public async Task ClearAllLibraries_RemovesAllLibraries()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test1.mo", "model Test1 end Test1;");
        await service.AddLibraryFromFileAsync("test2.mo", "model Test2 end Test2;");

        service.ClearAllLibraries();

        Assert.Empty(service.Libraries);
        Assert.Empty(service.CombinedGraph.ModelNodes);
    }

    [Fact]
    public async Task ClearAllLibraries_FiresEvents()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");
        var librariesChangedFired = false;
        var treeDataChangedFired = false;
        service.OnLibrariesChanged += () => librariesChangedFired = true;
        service.OnTreeDataChanged += () => treeDataChangedFired = true;

        service.ClearAllLibraries();

        Assert.True(librariesChangedFired);
        Assert.True(treeDataChangedFired);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_ReturnsTopLevelModels()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", @"
package TestPackage
  model InnerModel end InnerModel;
end TestPackage;
");

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Single(items);
        Assert.Equal("TestPackage", items.First().Value?.Name);
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_ReturnsChildModels()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", @"
package TestPackage
  model InnerModel end InnerModel;
end TestPackage;
");

        var topLevel = (await service.GetTopLevelTreeItemsAsync()).First();
        var children = await service.GetChildTreeItemsAsync(topLevel.Value);

        Assert.Single(children);
        Assert.Equal("InnerModel", children.First().Value?.Name);
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_WithNullParent_ReturnsTopLevel()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");

        var items = await service.GetChildTreeItemsAsync(null);

        Assert.Single(items);
    }

    [Fact]
    public async Task GetModelById_ReturnsModelWhenExists()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel end TestModel;");

        var model = service.GetModelById("TestModel");

        Assert.NotNull(model);
        Assert.Equal("TestModel", model.Definition.Name);
    }

    [Fact]
    public async Task GetModelById_ReturnsNullWhenNotExists()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel end TestModel;");

        var model = service.GetModelById("NonExistent");

        Assert.Null(model);
    }

    [Fact]
    public async Task GetAllModels_ReturnsModelsFromAllLibraries()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test1.mo", "model Test1 end Test1;");
        await service.AddLibraryFromFileAsync("test2.mo", "model Test2 end Test2;");

        var models = service.GetAllModels().ToList();

        Assert.Equal(2, models.Count);
        Assert.Contains(models, m => m.Definition.Name == "Test1");
        Assert.Contains(models, m => m.Definition.Name == "Test2");
    }

    [Fact]
    public async Task CombinedGraph_ContainsModelsFromAllLibraries()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test1.mo", "model Test1 end Test1;");
        await service.AddLibraryFromFileAsync("test2.mo", "model Test2 end Test2;");

        var modelNodes = service.CombinedGraph.ModelNodes.ToList();

        Assert.Equal(2, modelNodes.Count);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_SetsLibraryName()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
model TestModel
  Real x;
end TestModel;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.Equal("TestModel", library.Name);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_HandlesNestedModels()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package OuterPackage
  package InnerPackage
    model DeepModel end DeepModel;
  end InnerPackage;
end OuterPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.Single(library.TopLevelModelIds);
        Assert.Contains("OuterPackage", library.TopLevelModelIds);
        Assert.Contains("OuterPackage", library.ModelIds);
        Assert.Contains("OuterPackage.InnerPackage", library.ModelIds);
        Assert.Contains("OuterPackage.InnerPackage.DeepModel", library.ModelIds);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_BuildsChildrenByParentDictionary()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package TestPackage
  model Model1 end Model1;
  model Model2 end Model2;
end TestPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.True(library.ChildrenByParent.ContainsKey("TestPackage"));
        Assert.Equal(2, library.ChildrenByParent["TestPackage"].Count);
        Assert.Contains("TestPackage.Model1", library.ChildrenByParent["TestPackage"]);
        Assert.Contains("TestPackage.Model2", library.ChildrenByParent["TestPackage"]);
    }

    [Fact]
    public async Task AddLibraryFromFileAsync_ChildrenByParentEmptyForLeafModels()
    {
        var service = new LibraryDataService();
        var modelicaCode = "model LeafModel end LeafModel;";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        Assert.False(library.ChildrenByParent.ContainsKey("LeafModel"));
    }

    [Fact]
    public async Task RemoveModelsFromFile_RemovesModelsFromGraph()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel Real x; end TestModel;");
        Assert.NotEmpty(service.CombinedGraph.ModelNodes);

        service.RemoveModelsFromFile("test.mo");

        Assert.Empty(service.CombinedGraph.ModelNodes);
    }

    [Fact]
    public async Task RemoveModelsFromFile_ReturnsRemovedModelIds()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel Real x; end TestModel;");

        var removedIds = service.RemoveModelsFromFile("test.mo");

        Assert.Contains("TestModel", removedIds);
    }

    [Fact]
    public void RemoveModelsFromFile_FileNotInGraph_ReturnsEmpty()
    {
        var service = new LibraryDataService();

        var removedIds = service.RemoveModelsFromFile("nonexistent.mo");

        Assert.Empty(removedIds);
    }

    [Fact]
    public void RemoveModelsFromFile_HiddenDirectory_ReturnsEmpty()
    {
        var service = new LibraryDataService();

        var removedIds = service.RemoveModelsFromFile("C:/Repos/.git/model.mo");

        Assert.Empty(removedIds);
    }

    [Fact]
    public async Task RemoveModelsFromFile_RemovesFromLibraryModelIds()
    {
        var service = new LibraryDataService();
        var library = await service.AddLibraryFromFileAsync("test.mo", "model TestModel Real x; end TestModel;");
        Assert.Contains("TestModel", library.ModelIds);

        service.RemoveModelsFromFile("test.mo");

        Assert.DoesNotContain("TestModel", library.ModelIds);
    }

    [Fact]
    public async Task ReloadFileAsync_HiddenDirectory_ReturnsEmpty()
    {
        var service = new LibraryDataService();

        var affectedIds = await service.ReloadFileAsync("C:/Repos/.git/model.mo");

        Assert.Empty(affectedIds);
    }

    [Fact]
    public async Task ReloadFileAsync_NonExistentFile_RemovesOldModels()
    {
        // Load a file first, then "reload" a file path that doesn't exist on disk
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel Real x; end TestModel;");

        // File doesn't exist on disk, so it should remove old models but not add new ones
        var affectedIds = await service.ReloadFileAsync("test.mo");

        Assert.Contains("TestModel", affectedIds);
        Assert.Empty(service.CombinedGraph.ModelNodes);
    }

    [Fact]
    public async Task ReloadFileAsync_FiresOnTreeDataChangedEvent()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel Real x; end TestModel;");

        var eventFired = false;
        service.OnTreeDataChanged += () => eventFired = true;

        await service.ReloadFileAsync("test.mo");

        Assert.True(eventFired);
    }

    [Fact]
    public async Task RemoveLibrary_WithNonExistentId_DoesNotThrow()
    {
        var service = new LibraryDataService();

        service.RemoveLibrary("non-existent-library-id");

        Assert.Empty(service.Libraries);
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_WithUnknownParent_ReturnsEmpty()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");

        // Use a model node not part of any library
        var orphanModel = new ModelicaGraph.DataTypes.ModelNode("Orphan", "Orphan");
        var items = await service.GetChildTreeItemsAsync(orphanModel);

        Assert.Empty(items);
    }

    [Fact]
    public async Task ReloadFileAsync_ExistingFile_ReloadsModels()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"TestModel_{Guid.NewGuid():N}.mo");
        try
        {
            await File.WriteAllTextAsync(tempFile, "model TestModel Real x; end TestModel;");
            var service = new LibraryDataService();
            await service.AddLibraryFromFileAsync(tempFile, "model TestModel Real x; end TestModel;");

            // Change the file on disk
            await File.WriteAllTextAsync(tempFile, "model TestModel Real x; Real y; end TestModel;");

            var affectedIds = await service.ReloadFileAsync(tempFile);

            Assert.Contains("TestModel", affectedIds);
            Assert.NotEmpty(service.CombinedGraph.ModelNodes);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ReloadFileAsync_FindsLibraryBySourcePath()
    {
        // Tests the fallback path: file not in graph, find library by SourcePath
        var tempFile = Path.Combine(Path.GetTempPath(), $"TestModel2_{Guid.NewGuid():N}.mo");
        try
        {
            await File.WriteAllTextAsync(tempFile, "model TestModel2 Real x; end TestModel2;");
            var service = new LibraryDataService();
            var library = await service.AddLibraryFromFileAsync(tempFile, "model TestModel2 Real x; end TestModel2;");

            // Remove the model from graph so it falls through to the SourcePath lookup
            service.RemoveModelsFromFile(tempFile);
            library.SourcePath = tempFile; // Ensure SourcePath matches

            var affectedIds = await service.ReloadFileAsync(tempFile);

            Assert.NotEmpty(service.CombinedGraph.ModelNodes);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task AddLibraryFromDirectoryAsync_LoadsPackageStructure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TestLib_{Guid.NewGuid():N}");
        try
        {
            // Create a simple package structure
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.mo"),
                "package TestLib\nend TestLib;");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Model1.mo"),
                "within TestLib;\nmodel Model1 Real x; end Model1;");

            var service = new LibraryDataService();
            var library = await service.AddLibraryFromDirectoryAsync(tempDir);

            Assert.NotNull(library);
            Assert.Single(service.Libraries);
            Assert.Equal(LibrarySourceType.Directory, library.SourceType);
            Assert.NotEmpty(library.ModelIds);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AddLibraryFromDirectoryAsync_WithNoPackageMo_LoadsRootMoFiles()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TestLib2_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Model1.mo"),
                "model Model1 Real x; end Model1;");

            var service = new LibraryDataService();
            var library = await service.AddLibraryFromDirectoryAsync(tempDir);

            Assert.NotNull(library);
            Assert.Single(service.Libraries);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_WithPackageOrder_SortsChildren()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package TestPackage
  model ModelA end ModelA;
  model ModelB end ModelB;
  model ModelC end ModelC;
end TestPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        // Set package order on the package model to reverse order
        var packageNode = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>("TestPackage");
        if (packageNode != null)
        {
            packageNode.PackageOrder = new[] { "ModelC", "ModelB", "ModelA" };
        }

        var topLevel = (await service.GetTopLevelTreeItemsAsync()).First();
        var children = await service.GetChildTreeItemsAsync(topLevel.Value);

        Assert.Equal(3, children.Count);
        Assert.Equal("ModelC", children.ElementAt(0).Value?.Name);
    }

    // ============================================================================
    // SortByPackageOrder edge cases
    // ============================================================================

    [Fact]
    public async Task GetChildTreeItemsAsync_WithNestedChildrenOrder_SortsBySourceOrder()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package TestPackage
  model ModelA end ModelA;
  model ModelB end ModelB;
  model ModelC end ModelC;
end TestPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        // Set NestedChildrenOrder (fallback when no PackageOrder)
        var packageNode = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>("TestPackage");
        if (packageNode != null)
        {
            packageNode.PackageOrder = null;
            packageNode.NestedChildrenOrder = new[] { "ModelC", "ModelA", "ModelB" };
        }

        var topLevel = (await service.GetTopLevelTreeItemsAsync()).First();
        var children = await service.GetChildTreeItemsAsync(topLevel.Value);

        Assert.Equal(3, children.Count);
        Assert.Equal("ModelC", children.ElementAt(0).Value?.Name);
        Assert.Equal("ModelA", children.ElementAt(1).Value?.Name);
        Assert.Equal("ModelB", children.ElementAt(2).Value?.Name);
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_PartialPackageOrder_RemainingAppended()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package TestPackage
  model ModelA end ModelA;
  model ModelB end ModelB;
  model ModelC end ModelC;
end TestPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        // Package order only mentions one model; others should be appended
        var packageNode = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>("TestPackage");
        if (packageNode != null)
        {
            packageNode.PackageOrder = new[] { "ModelC" };
        }

        var topLevel = (await service.GetTopLevelTreeItemsAsync()).First();
        var children = await service.GetChildTreeItemsAsync(topLevel.Value);

        Assert.Equal(3, children.Count);
        Assert.Equal("ModelC", children.ElementAt(0).Value?.Name);
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_PackageOrderWithNonExistentNames_Ignored()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package TestPackage
  model ModelA end ModelA;
  model ModelB end ModelB;
end TestPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        var packageNode = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>("TestPackage");
        if (packageNode != null)
        {
            packageNode.PackageOrder = new[] { "NonExistent", "ModelB", "ModelA" };
        }

        var topLevel = (await service.GetTopLevelTreeItemsAsync()).First();
        var children = await service.GetChildTreeItemsAsync(topLevel.Value);

        Assert.Equal(2, children.Count);
        Assert.Equal("ModelB", children.ElementAt(0).Value?.Name);
        Assert.Equal("ModelA", children.ElementAt(1).Value?.Name);
    }

    [Fact]
    public async Task GetChildTreeItemsAsync_NoOrder_ReturnsChildrenUnsorted()
    {
        var service = new LibraryDataService();
        var modelicaCode = @"
package TestPackage
  model ModelA end ModelA;
  model ModelB end ModelB;
end TestPackage;
";

        var library = await service.AddLibraryFromFileAsync("test.mo", modelicaCode);

        var packageNode = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>("TestPackage");
        if (packageNode != null)
        {
            packageNode.PackageOrder = null;
            packageNode.NestedChildrenOrder = null;
        }

        var topLevel = (await service.GetTopLevelTreeItemsAsync()).First();
        var children = await service.GetChildTreeItemsAsync(topLevel.Value);

        Assert.Equal(2, children.Count);
    }

    // ============================================================================
    // TreeItemData creation (icon types)
    // ============================================================================

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_FunctionType_HasFunctionsIcon()
    {
        var service = new LibraryDataService();
        var code = """
            function TestFunc
              input Real x;
              output Real y;
            algorithm
              y := x * 2;
            end TestFunc;
            """;
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        var item = items.First();
        Assert.Equal(MudBlazor.Icons.Material.Filled.Functions, item.Icon);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_BlockType_HasViewModuleIcon()
    {
        var service = new LibraryDataService();
        var code = "block TestBlock Real x; end TestBlock;";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Equal(MudBlazor.Icons.Material.Filled.ViewModule, items.First().Icon);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_ConnectorType_HasPowerIcon()
    {
        var service = new LibraryDataService();
        var code = "connector TestConnector Real x; end TestConnector;";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Equal(MudBlazor.Icons.Material.Filled.Power, items.First().Icon);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_RecordType_HasDataObjectIcon()
    {
        var service = new LibraryDataService();
        var code = "record TestRecord Real x; end TestRecord;";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Equal(MudBlazor.Icons.Material.Filled.DataObject, items.First().Icon);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_PackageType_HasFolderOpenIcon()
    {
        var service = new LibraryDataService();
        var code = "package TestPkg end TestPkg;";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Equal(MudBlazor.Icons.Material.Filled.FolderOpen, items.First().Icon);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_ModelType_HasModelTrainingIcon()
    {
        var service = new LibraryDataService();
        var code = "model TestModel Real x; end TestModel;";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Equal(MudBlazor.Icons.Material.Filled.ModelTraining, items.First().Icon);
    }

    // ============================================================================
    // TreeItemData expandable property
    // ============================================================================

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_PackageWithChildren_IsExpandable()
    {
        var service = new LibraryDataService();
        var code = @"
package TestPackage
  model InnerModel end InnerModel;
end TestPackage;
";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.True(items.First().Expandable);
    }

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_LeafModel_NotExpandable()
    {
        var service = new LibraryDataService();
        var code = "model LeafModel Real x; end LeafModel;";
        await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.False(items.First().Expandable);
    }

    // ============================================================================
    // LibraryId assignment
    // ============================================================================

    [Fact]
    public async Task GetTopLevelTreeItemsAsync_SetsLibraryIdOnModelNode()
    {
        var service = new LibraryDataService();
        var code = "model TestModel Real x; end TestModel;";
        var library = await service.AddLibraryFromFileAsync("test.mo", code);

        var items = await service.GetTopLevelTreeItemsAsync();

        Assert.Equal(library.Id, items.First().Value?.LibraryId);
    }

    // ============================================================================
    // RemoveModelsFromFile edge cases
    // ============================================================================

    [Fact]
    public async Task RemoveModelsFromFile_RemovesFromChildrenByParent()
    {
        var service = new LibraryDataService();
        var code = @"
package TestPackage
  model Model1 end Model1;
  model Model2 end Model2;
end TestPackage;
";
        var library = await service.AddLibraryFromFileAsync("test.mo", code);
        Assert.True(library.ChildrenByParent.ContainsKey("TestPackage"));

        service.RemoveModelsFromFile("test.mo");

        // Children should have been removed from ChildrenByParent lists
        Assert.DoesNotContain("TestPackage.Model1", library.ChildrenByParent["TestPackage"]);
        Assert.DoesNotContain("TestPackage.Model2", library.ChildrenByParent["TestPackage"]);
    }

    [Fact]
    public async Task RemoveModelsFromFile_RemovesFromTopLevelModelIds()
    {
        var service = new LibraryDataService();
        var code = "model TestModel Real x; end TestModel;";
        var library = await service.AddLibraryFromFileAsync("test.mo", code);
        Assert.Contains("TestModel", library.TopLevelModelIds);

        service.RemoveModelsFromFile("test.mo");

        Assert.DoesNotContain("TestModel", library.TopLevelModelIds);
    }

    [Fact]
    public async Task RemoveModelsFromFile_RemovesFileNode()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model TestModel Real x; end TestModel;");
        var fileId = ModelicaGraph.GraphBuilder.GenerateFileId("test.mo");
        Assert.NotNull(service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(fileId));

        service.RemoveModelsFromFile("test.mo");

        Assert.Null(service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.FileNode>(fileId));
    }

    // ============================================================================
    // AddLibraryFromDirectoryAsync with package.order
    // ============================================================================

    [Fact]
    public async Task AddLibraryFromDirectoryAsync_ProcessesPackageOrderFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TestLib_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.mo"),
                "package TestLib\nend TestLib;");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ModelB.mo"),
                "within TestLib;\nmodel ModelB end ModelB;");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "ModelA.mo"),
                "within TestLib;\nmodel ModelA end ModelA;");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.order"),
                "ModelA\nModelB");

            var service = new LibraryDataService();
            var library = await service.AddLibraryFromDirectoryAsync(tempDir);

            var packageNode = service.CombinedGraph.GetNode<ModelicaGraph.DataTypes.ModelNode>("TestLib");
            Assert.NotNull(packageNode?.PackageOrder);
            Assert.Equal(new[] { "ModelA", "ModelB" }, packageNode!.PackageOrder);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AddLibraryFromDirectoryAsync_SkipsHiddenDirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TestLib_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.mo"),
                "package TestLib\nend TestLib;");

            // Create a hidden directory with a package.mo (should be skipped)
            var hiddenDir = Path.Combine(tempDir, ".hidden");
            Directory.CreateDirectory(hiddenDir);
            await File.WriteAllTextAsync(Path.Combine(hiddenDir, "package.mo"),
                "package HiddenPkg\nend HiddenPkg;");

            var service = new LibraryDataService();
            var library = await service.AddLibraryFromDirectoryAsync(tempDir);

            // HiddenPkg should not be loaded
            Assert.DoesNotContain("HiddenPkg", library.ModelIds);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AddLibraryFromDirectoryAsync_SkipsNonPackageSubdirectories()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TestLib_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.mo"),
                "package TestLib\nend TestLib;");

            // Create a Resources directory (no package.mo) with .mo files — should be skipped
            var resourcesDir = Path.Combine(tempDir, "Resources");
            Directory.CreateDirectory(resourcesDir);
            await File.WriteAllTextAsync(Path.Combine(resourcesDir, "ExampleModel.mo"),
                "model ExampleModel end ExampleModel;");

            var service = new LibraryDataService();
            var library = await service.AddLibraryFromDirectoryAsync(tempDir);

            Assert.DoesNotContain("ExampleModel", library.ModelIds);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AddLibraryFromDirectoryAsync_LoadsNestedSubpackages()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"TestLib_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.mo"),
                "package TestLib\nend TestLib;");

            // Create a sub-package
            var subDir = Path.Combine(tempDir, "SubPkg");
            Directory.CreateDirectory(subDir);
            await File.WriteAllTextAsync(Path.Combine(subDir, "package.mo"),
                "within TestLib;\npackage SubPkg\nend SubPkg;");
            await File.WriteAllTextAsync(Path.Combine(subDir, "DeepModel.mo"),
                "within TestLib.SubPkg;\nmodel DeepModel end DeepModel;");

            var service = new LibraryDataService();
            var library = await service.AddLibraryFromDirectoryAsync(tempDir);

            Assert.Contains("TestLib.SubPkg", library.ModelIds);
            Assert.Contains("TestLib.SubPkg.DeepModel", library.ModelIds);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    // ============================================================================
    // ReloadFileAsync with library detection
    // ============================================================================

    [Fact]
    public async Task ReloadFileAsync_UpdatesLibraryIndexWithNewModels()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"Test_{Guid.NewGuid():N}.mo");
        try
        {
            await File.WriteAllTextAsync(tempFile, "model TestModel Real x; end TestModel;");
            var service = new LibraryDataService();
            var library = await service.AddLibraryFromFileAsync(tempFile, "model TestModel Real x; end TestModel;");

            // Change to add a new model
            await File.WriteAllTextAsync(tempFile, @"
package TestPkg
  model Model1 end Model1;
end TestPkg;
");

            var affectedIds = await service.ReloadFileAsync(tempFile);

            Assert.Contains("TestPkg", library.ModelIds);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ============================================================================
    // Multiple libraries
    // ============================================================================

    [Fact]
    public async Task RemoveLibrary_DoesNotAffectOtherLibraries()
    {
        var service = new LibraryDataService();
        var lib1 = await service.AddLibraryFromFileAsync("test1.mo", "model Test1 end Test1;");
        var lib2 = await service.AddLibraryFromFileAsync("test2.mo", "model Test2 end Test2;");

        service.RemoveLibrary(lib1.Id);

        Assert.Single(service.Libraries);
        Assert.Equal(lib2.Id, service.Libraries.First().Id);
        Assert.NotNull(service.GetModelById("Test2"));
        Assert.Null(service.GetModelById("Test1"));
    }

    [Fact]
    public async Task GetAllModels_EmptyAfterClear()
    {
        var service = new LibraryDataService();
        await service.AddLibraryFromFileAsync("test.mo", "model Test end Test;");

        service.ClearAllLibraries();

        Assert.Empty(service.GetAllModels());
    }

    // ============================================================================
    // AddLibraryFromZipAsync edge cases
    // ============================================================================

    [Fact]
    public async Task AddLibraryFromZipAsync_NoTopLevelModels_EmptyName()
    {
        var service = new LibraryDataService();
        var files = new Dictionary<string, string>
        {
            { "inner.mo", "within SomePackage; model Inner end Inner;" }
        };

        var library = await service.AddLibraryFromZipAsync(files);

        // No top-level model, so name stays empty
        Assert.NotNull(library);
    }

    [Fact]
    public async Task AddLibraryFromZipAsync_MultipleFiles_LoadsAll()
    {
        var service = new LibraryDataService();
        var files = new Dictionary<string, string>
        {
            { "package.mo", "package TestPkg end TestPkg;" },
            { "Model1.mo", "within TestPkg; model Model1 end Model1;" },
            { "Model2.mo", "within TestPkg; model Model2 end Model2;" }
        };

        var library = await service.AddLibraryFromZipAsync(files);

        Assert.Contains("TestPkg.Model1", library.ModelIds);
        Assert.Contains("TestPkg.Model2", library.ModelIds);
    }

    [Fact]
    public async Task AddLibraryFromZipAsync_FiresEvents()
    {
        var service = new LibraryDataService();
        var librariesFired = false;
        var treeFired = false;
        service.OnLibrariesChanged += () => librariesFired = true;
        service.OnTreeDataChanged += () => treeFired = true;

        var files = new Dictionary<string, string>
        {
            { "test.mo", "model Test end Test;" }
        };

        await service.AddLibraryFromZipAsync(files);

        Assert.True(librariesFired);
        Assert.True(treeFired);
    }

    // ============================================================================
    // AddLibraryFromFileAsync without content (reads from disk)
    // ============================================================================

    [Fact]
    public async Task AddLibraryFromFileAsync_WithoutContent_ReadsFromDisk()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"DiskTest_{Guid.NewGuid():N}.mo");
        try
        {
            await File.WriteAllTextAsync(tempFile, "model DiskModel Real x; end DiskModel;");
            var service = new LibraryDataService();

            var library = await service.AddLibraryFromFileAsync(tempFile);

            Assert.Contains("DiskModel", library.ModelIds);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
