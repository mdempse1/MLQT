using ModelicaGraph;
using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;

namespace ModelicaGraph.Tests;

public class LoadSelectorAnalyzerTests
{
    private (DirectedGraph graph, ModelNode model) CreateGraphWithModel(string modelId, string modelicaCode)
    {
        var graph = new DirectedGraph();
        var model = new ModelNode(modelId, modelId.Split('.').Last(), modelicaCode);
        graph.AddNode(model);
        return (graph, model);
    }

    private List<ExternalResourceInfo> AnalyzeWithModelAnalyzer(string modelId, string modelicaCode, DirectedGraph? graph = null)
    {
        if (graph == null)
        {
            var result = CreateGraphWithModel(modelId, modelicaCode);
            graph = result.graph;
        }

        var parseTree = ModelicaParserHelper.Parse(modelicaCode);
        var analyzer = new ModelAnalyzer(modelId, graph);
        analyzer.Visit(parseTree);

        // Filter to only LoadSelector and LoadResourceParameter types, as these are the only
        // resources that the old LoadSelectorAnalyzer Pass 1 would have returned.
        // ModelAnalyzer also extracts other resource types (UriReference, LoadResource) via
        // its embedded ExternalResourceExtractor functionality, but those are not part of
        // the loadSelector parameter discovery logic.
        return analyzer.Resources
            .Where(r => r.ReferenceType == ResourceReferenceType.LoadSelector ||
                       r.ReferenceType == ResourceReferenceType.LoadResourceParameter)
            .ToList();
    }

    private List<ExternalResourceInfo> AnalyzeWithModificationAnalyzer(string modelId, string modelicaCode, DirectedGraph? graph = null)
    {
        if (graph == null)
        {
            var result = CreateGraphWithModel(modelId, modelicaCode);
            graph = result.graph;
        }

        var parseTree = ModelicaParserHelper.Parse(modelicaCode);
        var analyzer = new LoadSelectorModificationAnalyzer(modelId, graph);
        analyzer.Visit(parseTree);
        return analyzer.Resources;
    }

    [Fact]
    public void ParameterWithLoadSelector_DefaultValueNoName_ReturnsEmpty()
    {
        var code = @"
model CombiTimeTable
  parameter String fileName=""NoName"" ""File where matrix is stored""
    annotation (Dialog(
      loadSelector(filter=""Text files (*.txt);;MATLAB MAT-files (*.mat)"",
      caption=""Open file in which table is present"")));
end CombiTimeTable;";

        var (graph, model) = CreateGraphWithModel("Modelica.Blocks.Sources.CombiTimeTable", code);
        var resources = AnalyzeWithModelAnalyzer("Modelica.Blocks.Sources.CombiTimeTable", code, graph);

        // Default value is "NoName" which should be filtered out
        Assert.Empty(resources);

        // But the parameter should be registered as a loadSelector parameter
        Assert.True(model.LoadSelectorParameters.Count > 0);
        var paramList = model.LoadSelectorParameters;
        Assert.NotNull(paramList);
        Assert.Contains("fileName", paramList);
    }

    [Fact]
    public void ParameterWithLoadSelectorAndRealDefaultValue_IsDetected()
    {
        var code = @"
model CombiTimeTable
  parameter String fileName=""modelica://MyLib/Resources/data.mat"" ""File where matrix is stored""
    annotation (Dialog(
      loadSelector(filter=""MATLAB MAT-files (*.mat)"",
      caption=""Open file"")));
end CombiTimeTable;";

        var resources = AnalyzeWithModelAnalyzer("Modelica.Blocks.Sources.CombiTimeTable", code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/data.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadSelector, resources[0].ReferenceType);
        Assert.Equal("fileName", resources[0].ParameterName);
    }

    [Fact]
    public void ParameterWithoutLoadSelector_IsIgnored()
    {
        var code = @"
model TestModel
  parameter Real k=1.0 ""Gain"";
  parameter String name=""test"" ""Name"";
end TestModel;";

        var resources = AnalyzeWithModelAnalyzer("TestModel", code);

        Assert.Empty(resources);
    }

    [Fact]
    public void ModificationOfLoadSelectorParameter_IsDetected()
    {
        // First, create a model with the loadSelector parameter
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName"" ""File where matrix is stored""
    annotation (Dialog(
      loadSelector(filter=""MATLAB MAT-files (*.mat)"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);

        // Register the loadSelector parameter manually (simulating pass 1)
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        // Now create a model that uses CombiTimeTable with a modified fileName
        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table(
    fileName=""modelica://MyLib/Resources/data.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/data.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadSelector, resources[0].ReferenceType);
        Assert.Equal("fileName", resources[0].ParameterName);
    }

    [Fact]
    public void MultipleLoadSelectorParameters_AllDetected()
    {
        var code = @"
model DataSource
  parameter String fileName=""modelica://Lib/data.mat"" ""Data file""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
  parameter String configFile=""modelica://Lib/config.txt"" ""Config file""
    annotation (Dialog(loadSelector(filter=""*.txt"")));
end DataSource;";

        var resources = AnalyzeWithModelAnalyzer("MyLib.DataSource", code);

        Assert.Equal(2, resources.Count);
        Assert.Contains(resources, r => r.ParameterName == "fileName" && r.RawPath.EndsWith("data.mat"));
        Assert.Contains(resources, r => r.ParameterName == "configFile" && r.RawPath.EndsWith("config.txt"));
    }

    [Fact]
    public void LoadSelectorParameters_StoredOnModelNode()
    {
        var code = @"
model DataSource
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
  parameter String logFile=""NoName""
    annotation (Dialog(loadSelector(filter=""*.txt"")));
  parameter Real k=1.0 ""Plain parameter"";
end DataSource;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataSource", code);
        AnalyzeWithModelAnalyzer("MyLib.DataSource", code, graph);

        Assert.True(model.LoadSelectorParameters.Count > 0);
        var paramList = model.LoadSelectorParameters;
        Assert.NotNull(paramList);
        Assert.Equal(2, paramList.Count);
        Assert.Contains("fileName", paramList);
        Assert.Contains("logFile", paramList);
    }

    [Fact]
    public void ModificationWithAbsolutePath_IsDetected()
    {
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table(
    fileName=""C:/Data/experiment.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Single(resources);
        Assert.Equal("C:/Data/experiment.mat", resources[0].RawPath);
    }

    [Fact]
    public void ModificationWithEmptyString_IsFiltered()
    {
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table(fileName="""");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        // Empty strings should be filtered out
        Assert.Empty(resources);
    }

    [Fact]
    public void ParameterWithNoNameDefaultValue_IsFiltered()
    {
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table(fileName=""NoName"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        // "NoName" is a placeholder and should be filtered out
        Assert.Empty(resources);
    }

    [Fact]
    public void SaveSelectorAnnotation_IsIgnored()
    {
        // saveSelector is different from loadSelector - it's for saving files, not loading
        var code = @"
model DataExporter
  parameter String outputFile=""output.csv""
    annotation (Dialog(saveSelector(filter=""CSV files (*.csv)"")));
end DataExporter;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataExporter", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.DataExporter", code, graph);

        // saveSelector should not be detected as loadSelector
        Assert.Empty(resources);
        Assert.False(model.LoadSelectorParameters.Count > 0);
    }

    [Fact]
    public void MultipleInstancesOfSameComponent_AreDetected()
    {
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table1(
    fileName=""modelica://MyLib/Resources/data1.mat"");
  Modelica.Blocks.Sources.CombiTimeTable table2(
    fileName=""modelica://MyLib/Resources/data2.mat"");
  Modelica.Blocks.Sources.CombiTimeTable table3(
    fileName=""modelica://MyLib/Resources/data3.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Equal(3, resources.Count);
        Assert.Contains(resources, r => r.RawPath.EndsWith("data1.mat"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("data2.mat"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("data3.mat"));
    }

    [Fact]
    public void LoadSelectorWithGroupParameter_IsDetected()
    {
        var code = @"
model DataSource
  parameter String fileName=""modelica://MyLib/default.mat""
    annotation (Dialog(
      group=""Input"",
      loadSelector(filter=""MATLAB files (*.mat)"", caption=""Select data file"")));
end DataSource;";

        var resources = AnalyzeWithModelAnalyzer("MyLib.DataSource", code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/default.mat", resources[0].RawPath);
        Assert.Equal("fileName", resources[0].ParameterName);
    }

    [Fact]
    public void LoadSelectorWithEnableParameter_IsDetected()
    {
        var code = @"
model DataSource
  parameter Boolean useFile=true;
  parameter String fileName=""modelica://MyLib/data.mat""
    annotation (Dialog(
      enable=useFile,
      loadSelector(filter=""*.mat"")));
end DataSource;";

        var resources = AnalyzeWithModelAnalyzer("MyLib.DataSource", code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/data.mat", resources[0].RawPath);
    }

    [Fact]
    public void LoadSelectorInExtendedModel_IsDetected()
    {
        // A model that extends another and has loadSelector
        var code = @"
model SpecialTable
  extends Modelica.Blocks.Sources.CombiTimeTable;
  parameter String configFile=""modelica://MyLib/config.txt""
    annotation (Dialog(loadSelector(filter=""*.txt"")));
end SpecialTable;";

        var (graph, model) = CreateGraphWithModel("MyLib.SpecialTable", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.SpecialTable", code, graph);

        // Should detect the loadSelector in the extending model
        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/config.txt", resources[0].RawPath);
        Assert.Equal("configFile", resources[0].ParameterName);
    }

    [Fact]
    public void ModificationOfShortTypeName_IsDetected()
    {
        // When the component type uses a short name that needs resolution
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("MyLib.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  CombiTimeTable table(fileName=""modelica://MyLib/Resources/data.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/data.mat", resources[0].RawPath);
    }

    [Fact]
    public void LoadSelectorWithRelativePath_IsDetected()
    {
        var code = @"
model DataSource
  parameter String fileName=""../Resources/data.mat""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end DataSource;";

        var resources = AnalyzeWithModelAnalyzer("MyLib.DataSource", code);

        Assert.Single(resources);
        Assert.Equal("../Resources/data.mat", resources[0].RawPath);
    }

    [Fact]
    public void LoadSelectorWithTabParameter_IsDetected()
    {
        // Dialog can have tab parameter for grouping in dialogs
        var code = @"
model DataSource
  parameter String fileName=""modelica://MyLib/data.mat""
    annotation (Dialog(
      tab=""Files"",
      group=""Input Data"",
      loadSelector(filter=""*.mat"")));
end DataSource;";

        var resources = AnalyzeWithModelAnalyzer("MyLib.DataSource", code);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/data.mat", resources[0].RawPath);
    }

    [Fact]
    public void ParameterWithOnlyDialogAndNoLoadSelector_IsIgnored()
    {
        var code = @"
model TestModel
  parameter String name=""default""
    annotation (Dialog(group=""General""));
  parameter Real value=1.0
    annotation (Dialog(group=""General""));
end TestModel;";

        var (graph, model) = CreateGraphWithModel("MyLib.TestModel", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.TestModel", code, graph);

        Assert.Empty(resources);
        Assert.False(model.LoadSelectorParameters.Count > 0);
    }

    [Fact]
    public void LoadSelectorWithTableName_IsDetected()
    {
        // Real-world example from Modelica.Blocks.Sources.CombiTimeTable
        var code = @"
model CombiTimeTable
  parameter String tableName=""NoName"" ""Table name on file or in function usertab""
    annotation (Dialog(group=""Table data definition""));
  parameter String fileName=""NoName"" ""File where matrix is stored""
    annotation (Dialog(
      group=""Table data definition"",
      loadSelector(filter=""Text files (*.txt);;MATLAB MAT-files (*.mat)"",
                   caption=""Open file in which table is present"")));
end CombiTimeTable;";

        var (graph, model) = CreateGraphWithModel("Modelica.Blocks.Sources.CombiTimeTable", code);
        var resources = AnalyzeWithModelAnalyzer("Modelica.Blocks.Sources.CombiTimeTable", code, graph);

        // tableName has Dialog but no loadSelector, so only fileName should be recorded
        Assert.Empty(resources); // "NoName" is filtered
        var paramList = model.LoadSelectorParameters;
        Assert.NotNull(paramList);
        Assert.Single(paramList);
        Assert.Equal("fileName", paramList[0]);
    }

    [Fact]
    public void ModificationWithUnknownComponentType_NoResource()
    {
        // If we don't know about the component type, we shouldn't detect modifications
        var userCode = @"
model UserModel
  UnknownLib.SomeTable table(fileName=""modelica://MyLib/data.mat"");
end UserModel;";

        var graph = new DirectedGraph();
        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        // Should not detect because UnknownLib.SomeTable is not in the graph
        Assert.Empty(resources);
    }

    [Fact]
    public void ParameterWithBothLoadSelectorAndLoadResource_BothDetected()
    {
        // When a parameter has BOTH loadSelector annotation AND loadResource() default,
        // we should NOT add a resource from LoadSelectorAnalyzer because:
        // 1. The actual path inside loadResource() is captured by ExternalResourceExtractor
        // 2. Using the entire expression as the path would be incorrect
        var code = @"
model CombiTimeTable
  parameter String fileName=Modelica.Utilities.Files.loadResource(
    ""modelica://Modelica/Resources/Data/Utilities/sample.txt"") ""File where matrix is stored""
    annotation (Dialog(
      loadSelector(filter=""Text files (*.txt);;MATLAB MAT-files (*.mat)"",
      caption=""Open file in which table is present"")));
end CombiTimeTable;";

        var (graph, model) = CreateGraphWithModel("Modelica.Blocks.Sources.CombiTimeTable", code);
        var resources = AnalyzeWithModelAnalyzer("Modelica.Blocks.Sources.CombiTimeTable", code, graph);

        // No resources should be added here - the loadResource path is handled by ExternalResourceExtractor
        Assert.Empty(resources);

        // The parameter should be registered as BOTH a loadSelector parameter AND a loadResource parameter
        Assert.True(model.LoadSelectorParameters.Count > 0);
        var loadSelectorParams = model.LoadSelectorParameters;
        Assert.NotNull(loadSelectorParams);
        Assert.Contains("fileName", loadSelectorParams);

        Assert.True(model.LoadResourceParameters.Count > 0);
        var loadResourceParams = model.LoadResourceParameters;
        Assert.NotNull(loadResourceParams);
        Assert.Contains("fileName", loadResourceParams);
    }

    #region LoadResource Parameter Tests

    [Fact]
    public void ParameterWithLoadResourceDefault_IsDetected()
    {
        var code = @"
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://Lib/data.mat"");
end DataLoader;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataLoader", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.DataLoader", code, graph);

        // The loadResource call itself is captured by ExternalResourceExtractor, not this analyzer
        // This analyzer just stores the parameter for tracking modifications
        Assert.Empty(resources);

        // The parameter should be registered as a loadResource parameter
        Assert.True(model.LoadResourceParameters.Count > 0);
        var paramList = model.LoadResourceParameters;
        Assert.NotNull(paramList);
        Assert.Contains("fileName", paramList);
    }

    [Fact]
    public void ModificationOfLoadResourceParameter_IsDetected()
    {
        // First, create a model with the loadResource parameter
        var loaderCode = @"
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://Lib/default.mat"");
end DataLoader;";

        var graph = new DirectedGraph();
        var loaderModel = new ModelNode("MyLib.DataLoader", "DataLoader", loaderCode);
        graph.AddNode(loaderModel);

        // Register the loadResource parameter manually (simulating pass 1)
        loaderModel.LoadResourceParameters = new List<string> { "fileName" };

        // Now create a model that uses DataLoader with a modified fileName
        var userCode = @"
model UserModel
  MyLib.DataLoader loader(fileName=""modelica://MyLib/Resources/mydata.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/mydata.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResourceParameter, resources[0].ReferenceType);
        Assert.Equal("fileName", resources[0].ParameterName);
    }

    [Fact]
    public void LoadResourceParameterWithNoNameDefault_NoResource()
    {
        var code = @"
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource(""NoName"");
end DataLoader;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataLoader", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.DataLoader", code, graph);

        // No resource because "NoName" is filtered
        Assert.Empty(resources);

        // But the parameter should still be registered for tracking modifications
        Assert.True(model.LoadResourceParameters.Count > 0);
        var paramList = model.LoadResourceParameters;
        Assert.NotNull(paramList);
        Assert.Contains("fileName", paramList);
    }

    [Fact]
    public void LoadResourceModificationWithEmptyString_NoResource()
    {
        var loaderCode = @"
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://Lib/default.mat"");
end DataLoader;";

        var graph = new DirectedGraph();
        var loaderModel = new ModelNode("MyLib.DataLoader", "DataLoader", loaderCode);
        graph.AddNode(loaderModel);
        loaderModel.LoadResourceParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  MyLib.DataLoader loader(fileName="""");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        // Empty strings should be filtered out
        Assert.Empty(resources);
    }

    [Fact]
    public void MultipleLoadResourceParameters_AllDetected()
    {
        var code = @"
model MultiLoader
  parameter String file1 = Modelica.Utilities.Files.loadResource(""modelica://Lib/a.mat"");
  parameter String file2 = Modelica.Utilities.Files.loadResource(""modelica://Lib/b.mat"");
end MultiLoader;";

        var (graph, model) = CreateGraphWithModel("MyLib.MultiLoader", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.MultiLoader", code, graph);

        // Both parameters should be stored
        Assert.True(model.LoadResourceParameters.Count > 0);
        var paramList = model.LoadResourceParameters;
        Assert.NotNull(paramList);
        Assert.Equal(2, paramList.Count);
        Assert.Contains("file1", paramList);
        Assert.Contains("file2", paramList);
    }

    [Fact]
    public void LoadResourceAndLoadSelectorOnSameModel_BothDetected()
    {
        var code = @"
model ComboLoader
  parameter String dataFile = Modelica.Utilities.Files.loadResource(""modelica://Lib/data.mat"");
  parameter String configFile = ""config.txt""
    annotation (Dialog(loadSelector(filter=""*.txt"")));
end ComboLoader;";

        var (graph, model) = CreateGraphWithModel("MyLib.ComboLoader", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.ComboLoader", code, graph);

        // configFile should produce a resource (has real default value)
        Assert.Single(resources);
        Assert.Equal("config.txt", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadSelector, resources[0].ReferenceType);

        // Both types of parameters should be registered
        Assert.True(model.LoadResourceParameters.Count > 0);
        var loadResourceParams = model.LoadResourceParameters;
        Assert.NotNull(loadResourceParams);
        Assert.Contains("dataFile", loadResourceParams);

        Assert.True(model.LoadSelectorParameters.Count > 0);
        var loadSelectorParams = model.LoadSelectorParameters;
        Assert.NotNull(loadSelectorParams);
        Assert.Contains("configFile", loadSelectorParams);
    }

    [Fact]
    public void LoadResourceModification_ShortTypeName_IsResolved()
    {
        var loaderCode = @"
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://Lib/default.mat"");
end DataLoader;";

        var graph = new DirectedGraph();
        var loaderModel = new ModelNode("MyLib.DataLoader", "DataLoader", loaderCode);
        graph.AddNode(loaderModel);
        loaderModel.LoadResourceParameters = new List<string> { "fileName" };

        // User model uses short type name
        var userCode = @"
model UserModel
  DataLoader loader(fileName=""modelica://MyLib/Resources/data.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Single(resources);
        Assert.Equal("modelica://MyLib/Resources/data.mat", resources[0].RawPath);
        Assert.Equal(ResourceReferenceType.LoadResourceParameter, resources[0].ReferenceType);
    }

    [Fact]
    public void ShortFormLoadResource_IsDetected()
    {
        // When loadResource is imported and used without the full path
        var code = @"
model DataLoader
  parameter String fileName = loadResource(""modelica://Lib/data.mat"");
end DataLoader;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataLoader", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.DataLoader", code, graph);

        // The parameter should be registered as a loadResource parameter
        Assert.True(model.LoadResourceParameters.Count > 0);
        var paramList = model.LoadResourceParameters;
        Assert.NotNull(paramList);
        Assert.Contains("fileName", paramList);
    }

    [Fact]
    public void LoadResourceOnNonParameter_IsNotDetected()
    {
        // loadResource used in a non-parameter variable should not be tracked
        var code = @"
model DataUser
  String fileName = Modelica.Utilities.Files.loadResource(""modelica://Lib/data.mat"");
end DataUser;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataUser", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.DataUser", code, graph);

        // Should not be detected because it's not a parameter
        Assert.Empty(resources);
        Assert.False(model.LoadResourceParameters.Count > 0);
    }

    [Fact]
    public void MultipleInstanceModificationsOfLoadResourceParameter_AllDetected()
    {
        var loaderCode = @"
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource(""modelica://Lib/default.mat"");
end DataLoader;";

        var graph = new DirectedGraph();
        var loaderModel = new ModelNode("MyLib.DataLoader", "DataLoader", loaderCode);
        graph.AddNode(loaderModel);
        loaderModel.LoadResourceParameters = new List<string> { "fileName" };

        var userCode = @"
model UserModel
  MyLib.DataLoader loader1(fileName=""modelica://MyLib/data1.mat"");
  MyLib.DataLoader loader2(fileName=""modelica://MyLib/data2.mat"");
  MyLib.DataLoader loader3(fileName=""modelica://MyLib/data3.mat"");
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        Assert.Equal(3, resources.Count);
        Assert.All(resources, r => Assert.Equal(ResourceReferenceType.LoadResourceParameter, r.ReferenceType));
        Assert.Contains(resources, r => r.RawPath.EndsWith("data1.mat"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("data2.mat"));
        Assert.Contains(resources, r => r.RawPath.EndsWith("data3.mat"));
    }

    [Fact]
    public void LoadResourceModificationWithStandardLibrary_NoDuplicateResourceCreated()
    {
        // When a modification uses Modelica.Utilities.Files.loadResource(),
        // ExternalResourceExtractor will already capture the resource.
        // LoadSelectorAnalyzer should NOT add another entry to avoid duplicates.
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        // User uses loadResource() to specify the file - ExternalResourceExtractor will capture this
        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table(
    fileName=Modelica.Utilities.Files.loadResource(""modelica://MyLib/Resources/data.mat""));
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        // LoadSelectorAnalyzer should NOT add an entry when the modification uses loadResource()
        // because ExternalResourceExtractor will already capture it as LoadResource type
        Assert.Empty(resources);
    }

    [Fact]
    public void LoadResourceModification_NoDuplicateResourceCreated()
    {
        // When a modification uses loadResource(), ExternalResourceExtractor will already
        // capture the resource as LoadResource type. LoadSelectorAnalyzer should NOT
        // add another entry to avoid duplicates.
        var tableCode = @"
model CombiTimeTable
  parameter String fileName=""NoName""
    annotation (Dialog(loadSelector(filter=""*.mat"")));
end CombiTimeTable;";

        var graph = new DirectedGraph();
        var tableModel = new ModelNode("Modelica.Blocks.Sources.CombiTimeTable", "CombiTimeTable", tableCode);
        graph.AddNode(tableModel);
        tableModel.LoadSelectorParameters = new List<string> { "fileName" };

        // User uses loadResource() to specify the file - ExternalResourceExtractor will capture this
        var userCode = @"
model UserModel
  Modelica.Blocks.Sources.CombiTimeTable table(
    fileName=ModelicaServices.ExternalReferences.loadResource(""modelica://MyLib/Resources/data.mat""));
end UserModel;";

        var userModel = new ModelNode("MyLib.UserModel", "UserModel", userCode);
        graph.AddNode(userModel);

        var resources = AnalyzeWithModificationAnalyzer("MyLib.UserModel", userCode, graph);

        // LoadSelectorAnalyzer should NOT add an entry when the modification uses loadResource()
        // because ExternalResourceExtractor will already capture it as LoadResource type
        Assert.Empty(resources);
    }

    [Fact]
    public void ModelicaServicesLoadResource_IsDetected()
    {
        // Some users use ModelicaServices.ExternalReferences.loadResource instead of
        // Modelica.Utilities.Files.loadResource - both should be detected
        var code = @"
model DataLoader
  parameter String fileName = ModelicaServices.ExternalReferences.loadResource(""modelica://Lib/data.mat"");
end DataLoader;";

        var (graph, model) = CreateGraphWithModel("MyLib.DataLoader", code);
        var resources = AnalyzeWithModelAnalyzer("MyLib.DataLoader", code, graph);

        // The parameter should be registered as a loadResource parameter
        Assert.True(model.LoadResourceParameters.Count > 0);
        var paramList = model.LoadResourceParameters;
        Assert.NotNull(paramList);
        Assert.Contains("fileName", paramList);
    }

    [Fact]
    public void PropagatedParameterViaVariableReference_NoResource()
    {
        // When a component's parameter is set via a variable reference (not a loadResource call
        // or string literal), LoadSelectorAnalyzer should not detect it as a resource.
        // The loadResource call in the outer parameter will be captured by ExternalResourceExtractor.

        // Inner model has a plain String parameter (no default, no loadSelector)
        var innerCode = @"
model InnerModel
  parameter String fileName;
end InnerModel;";

        var graph = new DirectedGraph();
        var innerModel = new ModelNode("MyLib.InnerModel", "InnerModel", innerCode);
        graph.AddNode(innerModel);
        // No LoadSelectorParameters or LoadResourceParameters registered

        // Outer model uses loadResource for its parameter and propagates via variable reference
        var outerCode = @"
model OuterModel
  parameter String dataFile = ModelicaServices.ExternalReferences.loadResource(
    ""modelica://MyLib/Resources/Data/experiment.mat"");
  MyLib.InnerModel inner(fileName=dataFile);
end OuterModel;";

        var outerModel = new ModelNode("MyLib.OuterModel", "OuterModel", outerCode);
        graph.AddNode(outerModel);

        var resources = AnalyzeWithModelAnalyzer("MyLib.OuterModel", outerCode, graph);

        // LoadSelectorAnalyzer should NOT detect any resources here because:
        // 1. InnerModel.fileName is not a loadSelector or loadResource parameter
        // 2. The modification fileName=dataFile is a variable reference, not a resource path
        // The loadResource call in OuterModel.dataFile is captured by ExternalResourceExtractor
        Assert.Empty(resources);

        // The outer model should register dataFile as a loadResource parameter
        Assert.True(outerModel.LoadResourceParameters.Count > 0);
        Assert.Contains("dataFile", outerModel.LoadResourceParameters);
    }

    [Fact]
    public void LinkedParameterViaVariableReference_NoResource()
    {
        // When a component's parameter is set via a variable reference (not a loadResource call
        // or string literal), LoadSelectorAnalyzer should not detect it as a resource.
        // The loadResource call in the outer parameter will be captured by ExternalResourceExtractor.

        // Inner model has a plain String parameter (no default, no loadSelector)
        var innerCode = @"
model InnerModel
  parameter String fileName;
end InnerModel;";

        var graph = new DirectedGraph();
        var innerModel = new ModelNode("MyLib.InnerModel", "InnerModel", innerCode);
        graph.AddNode(innerModel);
        // No LoadSelectorParameters or LoadResourceParameters registered

        // Outer model uses loadResource for its parameter and propagates via variable reference
        var outerCode = @"
model OuterModel
  MyLib.Data data;
  MyLib.InnerModel inner(fileName=data.dataFile);
end OuterModel;";

        var outerModel = new ModelNode("MyLib.OuterModel", "OuterModel", outerCode);
        graph.AddNode(outerModel);

        // Data record uses loadResource for its parameter and propagates via variable reference
        var dataCode = @"
record Data
  parameter String dataFile = ModelicaServices.ExternalReferences.loadResource(
    ""modelica://MyLib/Resources/Data/experiment.mat"");
  MyLib.InnerModel inner(fileName=dataFile);
end Data;";

        var dataModel = new ModelNode("MyLib.Data", "Data", dataCode);
        graph.AddNode(dataModel);

        var resources = AnalyzeWithModelAnalyzer("MyLib.OuterModel", outerCode, graph);
        resources.AddRange(AnalyzeWithModelAnalyzer("MyLib.Data", dataCode, graph));

        // LoadSelectorAnalyzer should NOT detect any resources here because:
        // 1. InnerModel.fileName is not a loadSelector or loadResource parameter
        // 2. The modification fileName=dataFile is a variable reference, not a resource path
        // The loadResource call in OuterModel.dataFile is captured by ExternalResourceExtractor
        Assert.Empty(resources);

        // The data model should register dataFile as a loadResource parameter
        Assert.Contains("dataFile", dataModel.LoadResourceParameters);

        // The outermodel should not register dataFile as a loadResource parameter
        Assert.Empty(outerModel.LoadResourceParameters);
    }
    #endregion
}
