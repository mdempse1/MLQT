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
}
