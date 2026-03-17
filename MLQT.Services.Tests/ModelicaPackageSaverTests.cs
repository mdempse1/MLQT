using MLQT.Services.Helpers;
using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.Helpers;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the ModelicaPackageSaver class.
/// </summary>
public class ModelicaPackageSaverTests : IDisposable
{
    private readonly List<string> _tempDirectories = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "ModelicaPackageSaverTest_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return path;
    }

    private DirectedGraph CreateGraphWithSingleModel(string modelName, string modelCode)
    {
        var graph = new DirectedGraph();
        var parsedCode = ModelicaParserHelper.Parse(modelCode);
        var definition = new ModelDefinition(modelName, modelCode) { ParsedCode = parsedCode };
        var modelNode = new ModelNode(modelName, definition);
        modelNode.ClassType = "model";
        modelNode.ParentModelName = "";
        modelNode.CanBeStoredStandalone = true;
        graph.AddNode(modelNode);
        return graph;
    }

    private DirectedGraph CreateGraphWithPackage(string packageName, string packageCode, List<(string name, string code, string classType)> children)
    {
        var graph = new DirectedGraph();

        // Create package node
        var parsedCode = ModelicaParserHelper.Parse(packageCode);
        var definition = new ModelDefinition(packageName, packageCode) { ParsedCode = parsedCode };
        var packageNode = new ModelNode(packageName, definition);
        packageNode.ClassType = "package";
        packageNode.ParentModelName = "";
        packageNode.CanBeStoredStandalone = true;
        graph.AddNode(packageNode);

        // Create child nodes
        foreach (var (name, code, classType) in children)
        {
            var childId = $"{packageName}.{name}";
            var childParsedCode = ModelicaParserHelper.Parse(code);
            var childDefinition = new ModelDefinition(name, code) { ParsedCode = childParsedCode };
            var childNode = new ModelNode(childId, childDefinition);
            childNode.ClassType = classType;
            childNode.ParentModelName = packageName;
            childNode.CanBeStoredStandalone = true;
            graph.AddNode(childNode);
        }

        return graph;
    }

    #region SaveLibraryToDirectoryWithResult Tests

    [Fact]
    public void SaveLibraryToDirectoryWithResult_ReturnsWrittenFiles()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var graph = CreateGraphWithPackage("TestPackage", packageCode, new List<(string, string, string)>());
        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        Assert.NotEmpty(result.WrittenFiles);
    }

    [Fact]
    public void SaveLibraryToDirectoryWithResult_ReturnsModelIdToFilePath()
    {
        var modelCode = "within;\nmodel TestModel\n  Real x;\nend TestModel;";
        var graph = CreateGraphWithSingleModel("TestModel", modelCode);
        var modelIds = new HashSet<string> { "TestModel" };
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        Assert.True(result.ModelIdToFilePath.ContainsKey("TestModel"));
        Assert.EndsWith("TestModel.mo", result.ModelIdToFilePath["TestModel"]);
    }

    [Fact]
    public void SaveLibraryToDirectoryWithResult_OnlyIncludesModelsInSet()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var children = new List<(string, string, string)>
        {
            ("Model1", "within TestPackage;\nmodel Model1\nend Model1;", "model"),
            ("Model2", "within TestPackage;\nmodel Model2\nend Model2;", "model")
        };
        var graph = CreateGraphWithPackage("TestPackage", packageCode, children);

        // Only include TestPackage and Model1 (not Model2)
        var modelIds = new HashSet<string> { "TestPackage", "TestPackage.Model1" };
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        var model1File = Path.Combine(outputDir, "TestPackage", "Model1.mo");
        var model2File = Path.Combine(outputDir, "TestPackage", "Model2.mo");
        Assert.True(File.Exists(model1File));
        Assert.False(File.Exists(model2File));
    }

    [Fact]
    public void SaveLibraryToDirectoryWithResult_ReturnsCreatedDirectories()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var graph = CreateGraphWithPackage("TestPackage", packageCode, new List<(string, string, string)>());
        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        Assert.NotEmpty(result.CreatedDirectories);
    }

    #endregion

    #region SaveLibraryToDirectoryWithResult Additional Coverage

    [Fact]
    public void SaveLibraryToDirectoryWithResult_ModelWithoutWithinClause_AddsWithinPrefix()
    {
        // Covers PreParseModelsParallel lines 138-143 (adding "within" prefix)
        // Use model code WITHOUT "within" prefix
        var modelCode = "model TestModel\n  Real x;\nend TestModel;";
        var graph = new DirectedGraph();
        var parsedCode = ModelicaParserHelper.Parse(modelCode);
        var definition = new ModelDefinition("TestModel", modelCode) { ParsedCode = parsedCode };
        var modelNode = new ModelNode("TestModel", definition);
        modelNode.ClassType = "model";
        modelNode.ParentModelName = "";
        modelNode.CanBeStoredStandalone = true;
        graph.AddNode(modelNode);

        var modelIds = new HashSet<string> { "TestModel" };
        var outputDir = CreateTempDirectory();

        // Should not throw and should create the file
        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        Assert.True(result.ModelIdToFilePath.ContainsKey("TestModel"));
        var content = File.ReadAllText(result.ModelIdToFilePath["TestModel"]);
        Assert.Contains("within", content);
    }

    [Fact]
    public void SaveLibraryToDirectoryWithResult_ChildModelWithoutWithinAndWithParent_AddsWithinParent()
    {
        // Covers PreParseModelsParallel line 139: "within Parent;" path
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var graph = new DirectedGraph();

        var parsedPkg = ModelicaParserHelper.Parse(packageCode);
        var pkgDef = new ModelDefinition("TestPackage", packageCode) { ParsedCode = parsedPkg };
        var pkgNode = new ModelNode("TestPackage", pkgDef);
        pkgNode.ClassType = "package";
        pkgNode.ParentModelName = "";
        pkgNode.CanBeStoredStandalone = true;
        graph.AddNode(pkgNode);

        // Child without "within" clause but with parent name
        var childCode = "model ChildModel\n  Real x;\nend ChildModel;";
        var childParsed = ModelicaParserHelper.Parse(childCode);
        var childDef = new ModelDefinition("ChildModel", childCode) { ParsedCode = childParsed };
        var childNode = new ModelNode("TestPackage.ChildModel", childDef);
        childNode.ClassType = "model";
        childNode.ParentModelName = "TestPackage";
        childNode.CanBeStoredStandalone = true;
        graph.AddNode(childNode);

        var modelIds = new HashSet<string> { "TestPackage", "TestPackage.ChildModel" };
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        var childFilePath = Path.Combine(outputDir, "TestPackage", "ChildModel.mo");
        Assert.True(File.Exists(childFilePath));
        var content = File.ReadAllText(childFilePath);
        Assert.Contains("within TestPackage", content);
    }

    #endregion

    #region Package with Children and package.order

    [Fact]
    public void SaveLibraryToDirectoryWithResult_PackageWithChildren_WritesPackageOrder()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var children = new List<(string, string, string)>
        {
            ("Model1", "within TestPackage;\nmodel Model1\nend Model1;", "model"),
            ("Model2", "within TestPackage;\nmodel Model2\nend Model2;", "model")
        };
        var graph = CreateGraphWithPackage("TestPackage", packageCode, children);
        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        var packageOrderFile = Path.Combine(outputDir, "TestPackage", "package.order");
        Assert.True(File.Exists(packageOrderFile));
        var orderContent = File.ReadAllLines(packageOrderFile);
        Assert.Contains("Model1", orderContent);
        Assert.Contains("Model2", orderContent);
    }

    [Fact]
    public void SaveLibraryToDirectoryWithResult_PackageWithStoredOrder_UsesStoredOrder()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var children = new List<(string, string, string)>
        {
            ("Model1", "within TestPackage;\nmodel Model1\nend Model1;", "model"),
            ("Model2", "within TestPackage;\nmodel Model2\nend Model2;", "model")
        };
        var graph = CreateGraphWithPackage("TestPackage", packageCode, children);

        // Set a stored package order (Model2 before Model1)
        var pkgNode = graph.ModelNodes.First(m => m.Id == "TestPackage");
        pkgNode.PackageOrder = new[] { "Model2", "Model1" };

        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        var packageOrderFile = Path.Combine(outputDir, "TestPackage", "package.order");
        var orderContent = File.ReadAllLines(packageOrderFile);
        // Stored order should be respected
        var idx1 = Array.IndexOf(orderContent, "Model2");
        var idx2 = Array.IndexOf(orderContent, "Model1");
        Assert.True(idx1 < idx2, "Model2 should come before Model1 per stored package.order");
    }

    [Fact]
    public void SaveLibraryToDirectoryWithResult_NonStandaloneChild_EmbeddedInParent()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var graph = new DirectedGraph();

        var parsedPkg = ModelicaParserHelper.Parse(packageCode);
        var pkgDef = new ModelDefinition("TestPackage", packageCode) { ParsedCode = parsedPkg };
        var pkgNode = new ModelNode("TestPackage", pkgDef);
        pkgNode.ClassType = "package";
        pkgNode.ParentModelName = "";
        pkgNode.CanBeStoredStandalone = true;
        graph.AddNode(pkgNode);

        // Non-standalone child (e.g., has 'replaceable' prefix)
        var childCode = "within TestPackage;\nmodel NestedModel\nend NestedModel;";
        var childParsed = ModelicaParserHelper.Parse(childCode);
        var childDef = new ModelDefinition("NestedModel", childCode) { ParsedCode = childParsed };
        var childNode = new ModelNode("TestPackage.NestedModel", childDef);
        childNode.ClassType = "model";
        childNode.ParentModelName = "TestPackage";
        childNode.CanBeStoredStandalone = false;
        graph.AddNode(childNode);

        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        // Non-standalone child should NOT have its own .mo file
        var nestedFile = Path.Combine(outputDir, "TestPackage", "NestedModel.mo");
        Assert.False(File.Exists(nestedFile));

        // But should be mapped to the package.mo file
        Assert.True(result.ModelIdToFilePath.ContainsKey("TestPackage.NestedModel"));
        Assert.EndsWith("package.mo", result.ModelIdToFilePath["TestPackage.NestedModel"]);
    }

    #endregion

    #region Short Class Definitions

    [Fact]
    public void SaveLibraryToDirectoryWithResult_ShortClassPackage_SavedAsMoFile()
    {
        // A short class package (package X = Y) should be saved as .mo, not as a directory
        var code = "within;\npackage ShortPkg = OtherPkg;";
        var graph = new DirectedGraph();
        var parsedCode = ModelicaParserHelper.Parse(code);
        var def = new ModelDefinition("ShortPkg", code) { ParsedCode = parsedCode };
        var node = new ModelNode("ShortPkg", def);
        node.ClassType = "package";
        node.ParentModelName = "";
        node.CanBeStoredStandalone = true;
        graph.AddNode(node);

        var modelIds = new HashSet<string> { "ShortPkg" };
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        // Should be saved as ShortPkg.mo, not ShortPkg/package.mo
        Assert.True(result.ModelIdToFilePath.ContainsKey("ShortPkg"));
        Assert.EndsWith("ShortPkg.mo", result.ModelIdToFilePath["ShortPkg"]);
        Assert.False(Directory.Exists(Path.Combine(outputDir, "ShortPkg")));
    }

    #endregion

    #region Nested Package Hierarchy

    [Fact]
    public void SaveLibraryToDirectoryWithResult_NestedPackages_CreatesSubdirectories()
    {
        var graph = new DirectedGraph();

        // Root package
        var rootCode = "within;\npackage RootPkg\nend RootPkg;";
        var rootParsed = ModelicaParserHelper.Parse(rootCode);
        var rootNode = new ModelNode("RootPkg", new ModelDefinition("RootPkg", rootCode) { ParsedCode = rootParsed });
        rootNode.ClassType = "package";
        rootNode.ParentModelName = "";
        rootNode.CanBeStoredStandalone = true;
        graph.AddNode(rootNode);

        // Sub-package
        var subCode = "within RootPkg;\npackage SubPkg\nend SubPkg;";
        var subParsed = ModelicaParserHelper.Parse(subCode);
        var subNode = new ModelNode("RootPkg.SubPkg", new ModelDefinition("SubPkg", subCode) { ParsedCode = subParsed });
        subNode.ClassType = "package";
        subNode.ParentModelName = "RootPkg";
        subNode.CanBeStoredStandalone = true;
        graph.AddNode(subNode);

        // Model inside sub-package
        var modelCode = "within RootPkg.SubPkg;\nmodel Leaf\nend Leaf;";
        var modelParsed = ModelicaParserHelper.Parse(modelCode);
        var leafNode = new ModelNode("RootPkg.SubPkg.Leaf", new ModelDefinition("Leaf", modelCode) { ParsedCode = modelParsed });
        leafNode.ClassType = "model";
        leafNode.ParentModelName = "RootPkg.SubPkg";
        leafNode.CanBeStoredStandalone = true;
        graph.AddNode(leafNode);

        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        Assert.True(Directory.Exists(Path.Combine(outputDir, "RootPkg")));
        Assert.True(Directory.Exists(Path.Combine(outputDir, "RootPkg", "SubPkg")));
        Assert.True(File.Exists(Path.Combine(outputDir, "RootPkg", "SubPkg", "Leaf.mo")));
    }

    #endregion

    #region Formatting Options

    [Fact]
    public void SaveLibraryToDirectoryWithResult_WithFormattingOptions_Succeeds()
    {
        var modelCode = "within;\nmodel TestModel\n  Real x;\nend TestModel;";
        var graph = CreateGraphWithSingleModel("TestModel", modelCode);
        var modelIds = new HashSet<string> { "TestModel" };
        var outputDir = CreateTempDirectory();

        // Enable all formatting options
        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, true, true, true, true);

        Assert.NotEmpty(result.WrittenFiles);
        Assert.True(result.ModelIdToFilePath.ContainsKey("TestModel"));
    }

    #endregion

    #region Package with NestedChildrenOrder

    [Fact]
    public void SaveLibraryToDirectoryWithResult_PackageWithNestedChildrenOrder_IncludesInPackageOrder()
    {
        var packageCode = "within;\npackage TestPackage\nend TestPackage;";
        var graph = new DirectedGraph();

        var parsedPkg = ModelicaParserHelper.Parse(packageCode);
        var pkgDef = new ModelDefinition("TestPackage", packageCode) { ParsedCode = parsedPkg };
        var pkgNode = new ModelNode("TestPackage", pkgDef);
        pkgNode.ClassType = "package";
        pkgNode.ParentModelName = "";
        pkgNode.CanBeStoredStandalone = true;
        pkgNode.NestedChildrenOrder = new[] { "InlineType", "InlineConst" };
        graph.AddNode(pkgNode);

        var modelIds = new HashSet<string> { "TestPackage" };
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        var packageOrderFile = Path.Combine(outputDir, "TestPackage", "package.order");
        Assert.True(File.Exists(packageOrderFile));
        var orderContent = File.ReadAllLines(packageOrderFile);
        Assert.Contains("InlineType", orderContent);
        Assert.Contains("InlineConst", orderContent);
    }

    #endregion

    #region Empty Model Set

    [Fact]
    public void SaveLibraryToDirectoryWithResult_EmptyModelSet_ReturnsEmptyResult()
    {
        var graph = new DirectedGraph();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, new HashSet<string>(), outputDir, false, false, false, false);

        Assert.Empty(result.WrittenFiles);
        Assert.Empty(result.ModelIdToFilePath);
        Assert.Empty(result.CreatedDirectories);
    }

    #endregion

    #region Package with Elements (package.order from parse tree)

    [Fact]
    public void SaveLibraryToDirectoryWithResult_PackageWithElementsNoStoredOrder_ExtractsElementNames()
    {
        // A package with a constant and a type — element names should be extracted for package.order
        var packageCode = "within;\npackage TestPackage\n  constant Real pi = 3.14;\n  type Velocity = Real;\nend TestPackage;";
        var graph = new DirectedGraph();
        var parsedPkg = ModelicaParserHelper.Parse(packageCode);
        var pkgDef = new ModelDefinition("TestPackage", packageCode) { ParsedCode = parsedPkg };
        var pkgNode = new ModelNode("TestPackage", pkgDef);
        pkgNode.ClassType = "package";
        pkgNode.ParentModelName = "";
        pkgNode.CanBeStoredStandalone = true;
        // No stored PackageOrder — should extract from parse tree
        graph.AddNode(pkgNode);

        // Add a child model so package.order is written
        var childCode = "within TestPackage;\nmodel Child\nend Child;";
        var childParsed = ModelicaParserHelper.Parse(childCode);
        var childNode = new ModelNode("TestPackage.Child", new ModelDefinition("Child", childCode) { ParsedCode = childParsed });
        childNode.ClassType = "model";
        childNode.ParentModelName = "TestPackage";
        childNode.CanBeStoredStandalone = true;
        graph.AddNode(childNode);

        var modelIds = graph.ModelNodes.Select(m => m.Id).ToHashSet();
        var outputDir = CreateTempDirectory();

        var result = ModelicaPackageSaver.SaveLibraryToDirectoryWithResult(
            graph, modelIds, outputDir, false, false, false, false);

        var packageOrderFile = Path.Combine(outputDir, "TestPackage", "package.order");
        Assert.True(File.Exists(packageOrderFile));
        var orderContent = File.ReadAllLines(packageOrderFile);
        Assert.Contains("Child", orderContent);
        // Element names from the package body should also be extracted
        Assert.Contains("pi", orderContent);
        Assert.Contains("Velocity", orderContent);
    }

    #endregion
}
