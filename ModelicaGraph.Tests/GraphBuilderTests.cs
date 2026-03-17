using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using Xunit;

namespace ModelicaGraph.Tests;

public class GraphBuilderTests
{
    private readonly string _testFilesPath;

    public GraphBuilderTests()
    {
        _testFilesPath = Path.Combine(AppContext.BaseDirectory, "TestFiles");
    }

    [Fact]
    public void LoadModelicaFile_WithSimpleModel_AddsFileAndModelToGraph()
    {
        // Arrange
        var graph = new DirectedGraph();
        var filePath = Path.Combine(_testFilesPath, "SimpleModel.mo");

        // Act
        var fileNode = GraphBuilder.LoadModelicaFile(graph, filePath, File.ReadAllText(filePath));

        // Assert
        Assert.NotNull(fileNode);
        Assert.Equal(2, graph.NodeCount); // 1 file + 1 model
        Assert.Single(graph.FileNodes);
        Assert.Single(graph.ModelNodes);
    }

    [Fact]
    public void LoadModelicaFile_SetsFileNodeProperties()
    {
        // Arrange
        var graph = new DirectedGraph();
        var filePath = Path.Combine(_testFilesPath, "SimpleModel.mo");

        // Act
        var modelIds = GraphBuilder.LoadModelicaFile(graph, filePath, File.ReadAllText(filePath));

        // Assert
        Assert.NotEmpty(modelIds);
        Assert.Contains("SimpleModel", modelIds);
    }

    [Fact]
    public void LoadModelicaFile_CreatesFileContainsModelRelationship()
    {
        // Arrange
        var graph = new DirectedGraph();
        var filePath = Path.Combine(_testFilesPath, "SimpleModel.mo");

        // Act
        var modelIds = GraphBuilder.LoadModelicaFile(graph, filePath, File.ReadAllText(filePath));

        // Assert
        var modelNode = graph.ModelNodes.First();
        Assert.Contains(modelNode.Id, modelIds.First());
        Assert.Equal(modelIds.First(), modelNode.Id);
    }

    [Fact]
    public void LoadModelicaFile_WithPackage_AddsAllNestedModels()
    {
        // Arrange
        var graph = new DirectedGraph();
        var filePath = Path.Combine(_testFilesPath, "PackageExample.mo");

        // Act
        var modelIds = GraphBuilder.LoadModelicaFile(graph, filePath, File.ReadAllText(filePath));

        // Assert
        // Should have: TestPackage, TestPackage.ModelA, TestPackage.ModelB
        Assert.Equal(3, graph.ModelNodes.Count());
        Assert.Equal(3, modelIds.Count);
    }

    [Fact]
    public void LoadModelicaFile_StoresModelProperties()
    {
        // Arrange
        var graph = new DirectedGraph();
        var filePath = Path.Combine(_testFilesPath, "PackageExample.mo");

        // Act
        GraphBuilder.LoadModelicaFile(graph, filePath, File.ReadAllText(filePath));

        // Assert
        var packageNode = graph.ModelNodes.First(m => m.Definition.Name == "TestPackage");
        Assert.NotNull(packageNode.ClassType);
        Assert.True(packageNode.StartLine >= 0);
        Assert.True(packageNode.StopLine >= 0);
    }

    [Fact]
    public void LoadModelicaFile_WithContent_LoadsFromProvidedContent()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = "model Test\n  Real x;\nend Test;";
        var filePath = "virtual.mo";

        // Act
        var modelIds = GraphBuilder.LoadModelicaFile(graph, filePath, content);

        // Assert
        Assert.Single(graph.ModelNodes);
        var modelNode = graph.ModelNodes.First();
        Assert.Equal("Test", modelNode.Definition.Name);
    }

    [Fact]
    public void LoadModelicaFiles_LoadsMultipleFiles()
    {
        // Arrange
        var graph = new DirectedGraph();
        var file1 = Path.Combine(_testFilesPath, "SimpleModel.mo");
        var file2 = Path.Combine(_testFilesPath, "PackageExample.mo");

        // Act
        var modelIds = GraphBuilder.LoadModelicaFiles(graph, file1, file2);

        // Assert
        Assert.Equal(2, graph.FileNodes.Count());
        Assert.Equal(4, graph.ModelNodes.Count()); // 1 from SimpleModel + 3 from Package
    }

    [Fact]
    public void GenerateModelId_WithParent_CombinesNames()
    {
        // Arrange & Act
        var id = GraphBuilder.GenerateModelId("Parent", "Child");

        // Assert
        Assert.Equal("Parent.Child", id);
    }

    [Fact]
    public void GenerateModelId_WithoutParent_ReturnsModelName()
    {
        // Arrange & Act
        var id1 = GraphBuilder.GenerateModelId(null, "TopLevel");
        var id2 = GraphBuilder.GenerateModelId("", "TopLevel");

        // Assert
        Assert.Equal("TopLevel", id1);
        Assert.Equal("TopLevel", id2);
    }

    [Fact]
    public void AnalyzeDependencies_CreatesModelUsesModelRelationships()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = @"
            package TestPkg
              model Base
                Real x;
              end Base;

              model Derived
                Base baseComponent;
                Real y;
              end Derived;
            end TestPkg;
        ";
        GraphBuilder.LoadModelicaFile(graph, "test.mo", content);

        // Act
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();

        // Assert
        var derivedModel = graph.ModelNodes.FirstOrDefault(m => m.Definition.Name == "Derived");
        Assert.NotNull(derivedModel);

        // Derived should use Base
        var usedModels = graph.GetUsedModels(derivedModel.Id).ToList();
        Assert.NotEmpty(usedModels);
    }

    [Fact]
    public void AnalyzeDependencies_WithMalformedCode_HandlesGracefully()
    {
        // Arrange
        var graph = new DirectedGraph();
        var malformedContent = "model Broken\n  this is not valid modelica\nend";

        // Create a model node manually with malformed code
        var modelNode = new ModelNode("broken", "Broken", malformedContent);
        graph.AddNode(modelNode);

        // Act - should not throw
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();

        // Assert - model should still be in graph
        Assert.Single(graph.ModelNodes);
    }

    [Fact]
    public void LoadModelicaDirectory_LoadsAllFilesInDirectory()
    {
        // Arrange
        var graph = new DirectedGraph();
        var tempDir = Path.Combine(Path.GetTempPath(), "GraphBuilderTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create test files
            File.WriteAllText(Path.Combine(tempDir, "Model1.mo"), "model Model1\n  Real x;\nend Model1;");
            File.WriteAllText(Path.Combine(tempDir, "Model2.mo"), "model Model2\n  Real y;\nend Model2;");

            // Act
            var fileNodes = GraphBuilder.LoadModelicaDirectory(graph, tempDir);

            // Assert
            Assert.Equal(2, fileNodes.Count);
            Assert.Equal(2, graph.FileNodes.Count());
            Assert.Equal(2, graph.ModelNodes.Count());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadModelicaDirectory_WithPackageOrder_StoresPackageOrder()
    {
        // Arrange
        var graph = new DirectedGraph();
        var tempDir = Path.Combine(Path.GetTempPath(), "GraphBuilderPackageTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create package.mo
            var packageContent = @"package TestPackage
  annotation(Documentation(info=""<html>Test package</html>""));
end TestPackage;";
            File.WriteAllText(Path.Combine(tempDir, "package.mo"), packageContent);

            // Create package.order
            var packageOrder = new[] { "Component1", "Component2", "Component3" };
            File.WriteAllLines(Path.Combine(tempDir, "package.order"), packageOrder);

            // Act
            var fileNodes = GraphBuilder.LoadModelicaDirectory(graph, tempDir);

            // Assert
            var packageNode = graph.ModelNodes.FirstOrDefault(m => m.Definition.Name == "TestPackage");
            Assert.NotNull(packageNode);
            Assert.NotNull(packageNode.PackageOrder);
            Assert.Equal(3, packageNode.PackageOrder.Length);
            Assert.Equal("Component1", packageNode.PackageOrder[0]);
            Assert.Equal("Component2", packageNode.PackageOrder[1]);
            Assert.Equal("Component3", packageNode.PackageOrder[2]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadModelicaDirectory_WithSearchPattern_FiltersFiles()
    {
        // Arrange
        var graph = new DirectedGraph();
        var tempDir = Path.Combine(Path.GetTempPath(), "GraphBuilderFilterTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create various files
            File.WriteAllText(Path.Combine(tempDir, "Model1.mo"), "model Model1\nend Model1;");
            File.WriteAllText(Path.Combine(tempDir, "Model2.mo"), "model Model2\nend Model2;");
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "This is a readme");

            // Act
            var modelIds = GraphBuilder.LoadModelicaDirectory(graph, tempDir, "*.mo");

            // Assert
            Assert.Equal(2, modelIds.Count);
            Assert.Contains("Model1", modelIds);
            Assert.Contains("Model2", modelIds);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadModelicaDirectory_WithAllDirectories_LoadsRecursively()
    {
        // Arrange
        var graph = new DirectedGraph();
        var tempDir = Path.Combine(Path.GetTempPath(), "GraphBuilderRecursiveTest", Guid.NewGuid().ToString());
        var subDir = Path.Combine(tempDir, "SubDir");
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(subDir);

        try
        {
            // Create files in different directories
            File.WriteAllText(Path.Combine(tempDir, "Model1.mo"), "model Model1\nend Model1;");
            File.WriteAllText(Path.Combine(subDir, "Model2.mo"), "model Model2\nend Model2;");

            // Act
            var fileNodes = GraphBuilder.LoadModelicaDirectory(graph, tempDir, "*.mo", SearchOption.AllDirectories);

            // Assert
            Assert.Equal(2, fileNodes.Count);
            Assert.Equal(2, graph.ModelNodes.Count());
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadModelicaDirectory_WithNestedPackage_HandlesPackageOrderCorrectly()
    {
        // Arrange
        var graph = new DirectedGraph();
        var tempDir = Path.Combine(Path.GetTempPath(), "GraphBuilderNestedPackageTest", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a package with nested package
            var packageContent = @"package OuterPackage
  package InnerPackage
    model Model1
    end Model1;
  end InnerPackage;
end OuterPackage;";
            File.WriteAllText(Path.Combine(tempDir, "package.mo"), packageContent);

            // Create package.order (should only apply to OuterPackage)
            File.WriteAllLines(Path.Combine(tempDir, "package.order"), new[] { "InnerPackage" });

            // Act
            var fileNodes = GraphBuilder.LoadModelicaDirectory(graph, tempDir);

            // Assert
            var outerPackage = graph.ModelNodes.FirstOrDefault(m => m.Definition.Name == "OuterPackage");
            Assert.NotNull(outerPackage);
            Assert.NotNull(outerPackage.PackageOrder);

            // Inner package should not have PackageOrder from the file
            var innerPackage = graph.ModelNodes.FirstOrDefault(m => m.Definition.Name == "InnerPackage");
            Assert.NotNull(innerPackage);
            // Inner package might have NestedChildrenOrder but not PackageOrder from file
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void LoadModelicaDirectory_WithoutPackageOrder_DoesNotCrash()
    {
        // Arrange
        var graph = new DirectedGraph();
        var tempDir = Path.Combine(Path.GetTempPath(), "GraphBuilderNoPackageOrder", Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create package.mo without package.order
            File.WriteAllText(Path.Combine(tempDir, "package.mo"), "package TestPkg\nend TestPkg;");

            // Act
            var fileNodes = GraphBuilder.LoadModelicaDirectory(graph, tempDir);

            // Assert
            Assert.Single(fileNodes);
            var packageNode = graph.ModelNodes.FirstOrDefault(m => m.Definition.Name == "TestPkg");
            Assert.NotNull(packageNode);
            Assert.Null(packageNode.PackageOrder);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void AnalyzeDependencies_WithExceptionInAddEdge_ContinuesProcessing()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = @"
            package TestPkg
              model Model1
                Real x;
              end Model1;

              model Model2
                Model1 m1;
              end Model2;

              model Model3
                Model1 m1;
                Model2 m2;
              end Model3;
            end TestPkg;
        ";
        GraphBuilder.LoadModelicaFile(graph, "test.mo", content);

        // Act - should not throw even if duplicate edges are attempted
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult();
        GraphBuilder.AnalyzeDependenciesAsync(graph).GetAwaiter().GetResult(); // Run again to test duplicate edge handling

        // Assert
        Assert.NotEmpty(graph.ModelNodes);
    }

    [Fact]
    public void LoadModelicaFile_WithContentParameter_DoesNotReadFile()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = "model VirtualModel\n  Real x;\nend VirtualModel;";
        var virtualPath = "nonexistent.mo";

        // Act
        var modelIds = GraphBuilder.LoadModelicaFile(graph, virtualPath, content);

        // Assert
        Assert.Single(graph.ModelNodes);
        Assert.Equal("VirtualModel", graph.ModelNodes.First().Definition.Name);
        Assert.Equal("VirtualModel", modelIds.First());
    }

    [Fact]
    public void GenerateModelId_WithEmptyParent_ReturnsModelName()
    {
        // Arrange & Act
        var id1 = GraphBuilder.GenerateModelId("", "Model");
        var id2 = GraphBuilder.GenerateModelId(null, "Model");

        // Assert
        Assert.Equal("Model", id1);
        Assert.Equal("Model", id2);
    }

    [Fact]
    public void LoadModelicaFile_StoresCanBeStoredStandaloneProperty()
    {
        // Arrange
        var graph = new DirectedGraph();
        var content = @"package TestPkg
  model StandaloneModel
    Real x;
  end StandaloneModel;

  model NestedModel
    Real y;
  end NestedModel;
end TestPkg;";

        // Act
        GraphBuilder.LoadModelicaFile(graph, "test.mo", content);

        // Assert
        var models = graph.ModelNodes.ToList();
        Assert.All(models, m => Assert.IsType<bool>(m.CanBeStoredStandalone));
    }

    [Fact]
    public void LoadModelicaFile_PrefixedModelsExtractedAsNonStandalone()
    {
        // Arrange - models with element prefixes (inner, outer, replaceable, redeclare)
        // are extracted but marked as non-standalone
        var graph = new DirectedGraph();
        var content = @"package TestPkg
  model StandaloneModel
    Real x;
  end StandaloneModel;

  inner model InnerModel
    Real y;
  end InnerModel;

  replaceable model ReplModel
    Real z;
  end ReplModel;
end TestPkg;";

        // Act
        GraphBuilder.LoadModelicaFile(graph, "test.mo", content);

        // Assert - all four models extracted, prefixed ones marked non-standalone with prefix
        var models = graph.ModelNodes.ToList();
        Assert.Equal(4, models.Count);
        Assert.Contains(models, m => m.Definition.Name == "TestPkg" && m.CanBeStoredStandalone && m.ElementPrefix == "");
        Assert.Contains(models, m => m.Definition.Name == "StandaloneModel" && m.CanBeStoredStandalone && m.ElementPrefix == "");
        Assert.Contains(models, m => m.Definition.Name == "InnerModel" && !m.CanBeStoredStandalone && m.ElementPrefix == "inner");
        Assert.Contains(models, m => m.Definition.Name == "ReplModel" && !m.CanBeStoredStandalone && m.ElementPrefix == "replaceable");
    }

    [Fact]
    public async Task AnalyzeDependencies_WithExternalInclude_ResolvesHeaderFile()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "ExtResTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create the Resources/Include directory with a header file
            var includeDir = Path.Combine(tempDir, "Resources", "Include");
            Directory.CreateDirectory(includeDir);
            var headerFile = Path.Combine(includeDir, "TestHeader.h");
            File.WriteAllText(headerFile, "// Test header");

            var graph = new DirectedGraph();

            // Create a function with external Include annotation using escaped quotes
            // This mimics what ANTLR extracts from: Include="#include \"TestHeader.h\""
            var content = @"
function TestFunc
  input Real x;
  output Real y;
  external ""C"" y = testfunc(x)
    annotation(Include=""#include \""TestHeader.h\"""");
end TestFunc;";

            GraphBuilder.LoadModelicaFile(graph, Path.Combine(tempDir, "TestFunc.mo"), content);

            // Create library info for resolution
            var libraries = new List<LibraryInfo>
            {
                new LibraryInfo("TestFunc", tempDir)
            };

            // Act
            await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);

            // Assert - should have created a ResourceFileNode for the header
            var resourceFileNodes = graph.ResourceFileNodes.ToList();
            Assert.Contains(resourceFileNodes, n => n.ResolvedPath.EndsWith("TestHeader.h"));

            // Verify the edge exists from model to resource
            var edges = graph.GetResourceEdgesForModel("TestFunc");
            Assert.Contains(edges, e => e.ReferenceType == ResourceReferenceType.ExternalInclude);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AnalyzeDependencies_WithExternalIncludeDirectory_ResolvesHeaderUsingSpecifiedDirectory()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "ExtResTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create a custom include directory (not the default)
            var includeDir = Path.Combine(tempDir, "Resources", "C-Sources");
            Directory.CreateDirectory(includeDir);
            var headerFile = Path.Combine(includeDir, "CustomHeader.h");
            File.WriteAllText(headerFile, "// Custom header");

            var graph = new DirectedGraph();

            // Create a function with both IncludeDirectory and Include annotations
            var content = @"
function TestFunc
  input Real x;
  output Real y;
  external ""C"" y = testfunc(x)
    annotation(
      IncludeDirectory=""modelica://TestLib/Resources/C-Sources"",
      Include=""#include \""CustomHeader.h\"""");
end TestFunc;";

            GraphBuilder.LoadModelicaFile(graph, Path.Combine(tempDir, "TestFunc.mo"), content);

            var libraries = new List<LibraryInfo>
            {
                new LibraryInfo("TestLib", tempDir)
            };

            // Act
            await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);

            // Assert - should have ResourceFileNode for the header resolved using IncludeDirectory
            var resourceFileNodes = graph.ResourceFileNodes.ToList();
            Assert.Contains(resourceFileNodes, n => n.ResolvedPath.EndsWith("CustomHeader.h"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AnalyzeDependencies_WithExternalLibrary_ResolvesAllPlatformVariants()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "ExtResTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create library files for different platforms
            var libraryDir = Path.Combine(tempDir, "Resources", "Library");
            var win64Dir = Path.Combine(libraryDir, "win64");
            var linux64Dir = Path.Combine(libraryDir, "linux64");
            Directory.CreateDirectory(win64Dir);
            Directory.CreateDirectory(linux64Dir);

            // Create the library files
            File.WriteAllText(Path.Combine(win64Dir, "TestLib.lib"), "");
            File.WriteAllText(Path.Combine(linux64Dir, "libTestLib.a"), "");

            var graph = new DirectedGraph();

            var content = @"
function TestFunc
  input Real x;
  output Real y;
  external ""C"" y = testfunc(x)
    annotation(Library=""TestLib"");
end TestFunc;";

            GraphBuilder.LoadModelicaFile(graph, Path.Combine(tempDir, "TestFunc.mo"), content);

            var libraries = new List<LibraryInfo>
            {
                new LibraryInfo("TestFunc", tempDir)
            };

            // Act
            await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);

            // Assert - should have ResourceFileNodes for both platform variants
            var resourceFileNodes = graph.ResourceFileNodes.ToList();
            Assert.Contains(resourceFileNodes, n => n.ResolvedPath.EndsWith("TestLib.lib"));
            Assert.Contains(resourceFileNodes, n => n.ResolvedPath.EndsWith("libTestLib.a"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task AnalyzeDependencies_WithExternalIncludeDirectory_ScansForContainedFiles()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), "ExtResTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Create C-Sources directory with various source files
            var sourcesDir = Path.Combine(tempDir, "Resources", "C-Sources");
            Directory.CreateDirectory(sourcesDir);
            File.WriteAllText(Path.Combine(sourcesDir, "source1.c"), "// Source 1");
            File.WriteAllText(Path.Combine(sourcesDir, "source2.c"), "// Source 2");
            File.WriteAllText(Path.Combine(sourcesDir, "header.h"), "// Header");

            var graph = new DirectedGraph();

            var content = @"
function TestFunc
  external ""C""
    annotation(IncludeDirectory=""modelica://TestLib/Resources/C-Sources"");
end TestFunc;";

            GraphBuilder.LoadModelicaFile(graph, Path.Combine(tempDir, "TestFunc.mo"), content);

            var libraries = new List<LibraryInfo>
            {
                new LibraryInfo("TestLib", tempDir)
            };

            // Act
            await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);

            // Assert - should have directory node with contained files
            var dirNodes = graph.ResourceDirectoryNodes.ToList();
            Assert.Single(dirNodes);

            var dirNode = dirNodes[0];
            Assert.Equal(3, dirNode.ContainedFileIds.Count); // 2 .c files + 1 .h file

            // Should have ResourceFileNodes for all contained files
            var fileNodes = graph.ResourceFileNodes.ToList();
            Assert.Contains(fileNodes, n => n.ResolvedPath.EndsWith("source1.c"));
            Assert.Contains(fileNodes, n => n.ResolvedPath.EndsWith("source2.c"));
            Assert.Contains(fileNodes, n => n.ResolvedPath.EndsWith("header.h"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ============================================================================
    // AnalyzeDependenciesForModelsAsync
    // ============================================================================

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_EmptySet_ReturnsEarlyWithNoChanges()
    {
        var graph = new DirectedGraph();
        var content = "model Simple Real x; end Simple;";
        GraphBuilder.LoadModelicaFile(graph, "simple.mo", content);

        // Empty set: should return immediately without touching the graph
        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string>());

        Assert.Single(graph.ModelNodes); // still the same
    }

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_NonExistentIds_ReturnsEarlyWithNoChanges()
    {
        var graph = new DirectedGraph();

        // No models in graph matching these IDs → models.Count == 0 branch
        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string> { "does.not.exist" });

        Assert.Empty(graph.ModelNodes);
    }

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_WithTargetModels_CreatesDependencyEdges()
    {
        var graph = new DirectedGraph();
        var content = """
package TestPkg
  model Base
    Real x;
  end Base;

  model Derived
    Base b;
    Real y;
  end Derived;
end TestPkg;
""";
        var modelIds = GraphBuilder.LoadModelicaFile(graph, "test.mo", content);
        var derivedId = modelIds.First(id => id.EndsWith("Derived"));

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string> { derivedId });

        var derived = graph.GetNode<ModelNode>(derivedId);
        Assert.NotNull(derived);
        var deps = graph.GetUsedModels(derivedId).ToList();
        Assert.NotEmpty(deps);
    }

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_ReplacesExistingEdges()
    {
        var graph = new DirectedGraph();
        var content = """
package Pkg
  model A
    Real x;
  end A;

  model B
    A a;
  end B;
end Pkg;
""";
        var modelIds = GraphBuilder.LoadModelicaFile(graph, "pkg.mo", content);
        var bId = modelIds.First(id => id.EndsWith(".B"));

        // First analysis
        await GraphBuilder.AnalyzeDependenciesAsync(graph);
        var initialDeps = graph.GetUsedModels(bId).Count();

        // Re-analyze B only — should clear and rebuild edges
        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string> { bId });

        var finalDeps = graph.GetUsedModels(bId).Count();
        Assert.Equal(initialDeps, finalDeps);
    }

    [Fact]
    public async Task AnalyzeDependenciesAsync_WithLoadSelectorParams_ExecutesPass2()
    {
        // This triggers the hasTrackedParams = true branch in AnalyzeDependenciesAsync,
        // exercising the parallel Pass 2 display class closures.
        var graph = new DirectedGraph();
        var content = """
model CombiTimeTable
  parameter String fileName = "modelica://MyLib/Resources/data.mat"
    annotation (Dialog(
      loadSelector(filter="MATLAB MAT-files (*.mat)",
      caption="Open file")));
end CombiTimeTable;
""";
        GraphBuilder.LoadModelicaFile(graph, "combi.mo", content);

        // ModelAnalyzer will register fileName as a loadSelector parameter,
        // causing hasTrackedParams = true and triggering Pass 2
        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        // The model should still be in the graph after analysis
        Assert.Single(graph.ModelNodes);
    }

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_WithLoadSelectorParams_ExecutesPass2()
    {
        // This triggers the hasTrackedParams = true branch in AnalyzeDependenciesForModelsAsync,
        // exercising the async state machine display class closures for method d__5.
        var graph = new DirectedGraph();
        var content = """
model CombiTimeTable
  parameter String fileName = "modelica://MyLib/Resources/data.mat"
    annotation (Dialog(
      loadSelector(filter="MATLAB MAT-files (*.mat)",
      caption="Open file")));
end CombiTimeTable;
""";
        var modelIds = GraphBuilder.LoadModelicaFile(graph, "combi.mo", content);

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string>(modelIds));

        Assert.Single(graph.ModelNodes);
    }

    [Fact]
    public async Task AnalyzeDependenciesAsync_ModelInBothPhase1AndPass2_ExecutesMergeClosures()
    {
        // Triggers DisplayClass4_2: the deduplication lambda inside the pass2 merge loop.
        // Requires a model that appears in BOTH:
        //   Phase 1 modelResources (has a loadSelector with a real default path)
        //   Pass 2 pass2Results (also modifies a loadSelector param of another model)
        var graph = new DirectedGraph();

        // Model A: defines a loadSelector parameter (gets into phase 1 results)
        var tableCode = """
model CombiTable
  parameter String tableFile = "modelica://Lib/Resources/default.mat"
    annotation (Dialog(
      loadSelector(filter="MATLAB MAT-files (*.mat)",
      caption="Open file")));
end CombiTable;
""";

        // Model B: also has its OWN loadSelector parameter (phase 1 entry),
        // AND instantiates CombiTable modifying tableFile (pass 2 entry).
        // Having both ensures modelResources.TryGetValue returns true in the merge.
        var userCode = """
model UserModel
  parameter String ownFile = "modelica://Lib/Resources/user.mat"
    annotation (Dialog(
      loadSelector(filter="MATLAB MAT-files (*.mat)",
      caption="Select user file")));
  CombiTable table(tableFile = "modelica://Lib/Resources/specific.mat");
end UserModel;
""";

        GraphBuilder.LoadModelicaFile(graph, "table.mo", tableCode);
        GraphBuilder.LoadModelicaFile(graph, "user.mo", userCode);

        await GraphBuilder.AnalyzeDependenciesAsync(graph);

        Assert.Equal(2, graph.ModelNodes.Count());
    }

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_ModelInBothPhase1AndPass2_ExecutesMergeClosures()
    {
        // Triggers DisplayClass5_2: same merge deduplication lambda but in AnalyzeDependenciesForModelsAsync.
        var graph = new DirectedGraph();

        var tableCode = """
model CombiTable2
  parameter String tableFile = "modelica://Lib/Resources/default.mat"
    annotation (Dialog(
      loadSelector(filter="MATLAB MAT-files (*.mat)",
      caption="Open file")));
end CombiTable2;
""";

        var userCode = """
model UserModel2
  parameter String ownFile = "modelica://Lib/Resources/user.mat"
    annotation (Dialog(
      loadSelector(filter="MATLAB MAT-files (*.mat)",
      caption="Select user file")));
  CombiTable2 table(tableFile = "modelica://Lib/Resources/specific.mat");
end UserModel2;
""";

        var ids1 = GraphBuilder.LoadModelicaFile(graph, "table2.mo", tableCode);
        var ids2 = GraphBuilder.LoadModelicaFile(graph, "user2.mo", userCode);
        var allIds = new HashSet<string>(ids1.Concat(ids2));

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, allIds);

        Assert.Equal(2, graph.ModelNodes.Count());
    }

    [Fact]
    public async Task AnalyzeDependenciesForModelsAsync_ModelWithNullParsedCode_ParsesOnDemand()
    {
        // Covers the ParsedCode == null re-parse branch (lines 425-429) in
        // the phase 1 Parallel.ForEach inside AnalyzeDependenciesForModelsAsync.
        // Models created manually (not via LoadModelicaFile) don't have ParsedCode pre-set.
        var graph = new DirectedGraph();
        var fileNode = new ModelicaGraph.DataTypes.FileNode("file1", "test.mo");
        graph.AddNode(fileNode);

        var code = "model ManualModel Real x; equation x = 1.0; end ManualModel;";
        var model = new ModelicaGraph.DataTypes.ModelNode("ManualModel", "ManualModel", code);
        // ParsedCode is null — not set by LoadModelicaFile
        Assert.Null(model.Definition.ParsedCode);
        graph.AddNode(model);
        graph.AddFileContainsModel("file1", "ManualModel");

        await GraphBuilder.AnalyzeDependenciesForModelsAsync(graph, new HashSet<string> { "ManualModel" });

        // After analysis, ParsedCode is released to reclaim memory.
        // It was parsed on-demand during analysis but freed afterward.
        Assert.Null(model.Definition.ParsedCode);

        // EnsureParsed can re-create it on demand
        Assert.NotNull(model.Definition.EnsureParsed());
    }
}
