# ModelicaGraph

A directed graph library for tracking relationships between Modelica files, models, dependencies, and external resources. Includes style checking integration.

## Overview

ModelicaGraph provides a graph structure to represent and query:

- **Files** that contain Modelica models
- **Models** and their dependencies on other models
- **External resources** (data files, C libraries, images) referenced by models
- **Style checking** against configurable rules

## Key Concepts

### Node Types

| Node | Purpose |
|------|---------|
| `FileNode` | Represents a Modelica file (`.mo`), tracks file path and contained models |
| `ModelNode` | Represents a Modelica model with definition, dependencies, and reverse dependencies |
| `ResourceFileNode` | Represents an external resource file (data, headers, libraries, images) |
| `ResourceDirectoryNode` | Represents an external resource directory (Include, Library, Source) |

### Relationships

- **File -> Model**: A file contains one or more models (`AddFileContainsModel`)
- **Model -> Model**: A model uses/depends on other models (`AddModelUsesModel`)
- **Model -> Resource**: A model references an external resource (`AddModelReferencesResource`)

### GraphBuilder

`GraphBuilder` is a static utility class that handles loading Modelica files into the graph, parsing them, extracting models, and analyzing dependencies.

### StyleChecking

`StyleChecking` provides a static method to run configurable style rules against model definitions, with `StyleCheckingSettings` controlling which rules are active.

## Usage

### Creating and Populating a Graph

```csharp
using ModelicaGraph;

var graph = new DirectedGraph();

// Load a single Modelica file (parses and extracts all models)
List<string> modelIds = GraphBuilder.LoadModelicaFile(graph, "Models.mo");

// Load from string content
List<string> modelIds = GraphBuilder.LoadModelicaFile(graph, "Models.mo", modelicaCode);

// Load multiple files
List<string> allModelIds = GraphBuilder.LoadModelicaFiles(graph, "Model1.mo", "Model2.mo");

// Load all .mo files from a directory
List<string> modelIds = GraphBuilder.LoadModelicaDirectory(graph, "path/to/library");
```

### Analyzing Dependencies

```csharp
// Analyze dependencies between all models in the graph
await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);

// Query dependencies for a specific model
var dependencies = graph.GetUsedModels("modelId");
var dependents = graph.GetModelUsedBy("modelId");
```

### Querying the Graph

```csharp
// Get all models in a file
var modelsInFile = graph.GetModelsInFile("fileId");

// Get models from a file by path
var models = GraphBuilder.GetModelsFromFile(graph, "Models.mo");

// Find a model by name
var model = GraphBuilder.GetModelByName(graph, "MyLibrary.MyModel");

// Get a dependency tree
var tree = graph.GetDependencyTree("modelId");

// Get all file nodes, model nodes, resource nodes
IEnumerable<FileNode> files = graph.FileNodes;
IEnumerable<ModelNode> models = graph.ModelNodes;
IEnumerable<ResourceFileNode> resourceFiles = graph.ResourceFileNodes;
IEnumerable<ResourceDirectoryNode> resourceDirs = graph.ResourceDirectoryNodes;
```

### Working with Nodes

```csharp
// FileNode
var fileNode = new FileNode("file1", "C:/models/MyModel.mo");
fileNode.Content = "model MyModel ... end MyModel;";
graph.AddNode(fileNode);

// ModelNode
var modelNode = new ModelNode("model1", "MyModel", "model MyModel ... end MyModel;");
graph.AddNode(modelNode);

// Access model definition
ModelDefinition def = modelNode.Definition;
string name = def.Name;
string code = def.ModelicaCode;

// Link file to model
graph.AddFileContainsModel("file1", "model1");

// Create model dependency
graph.AddModelUsesModel("usingModelId", "usedModelId");
```

### External Resources

```csharp
// Get or create resource nodes (deduplicates by resolved path)
ResourceFileNode resFile = graph.GetOrCreateResourceFileNode(@"C:\lib\data.mat");
ResourceDirectoryNode resDir = graph.GetOrCreateResourceDirectoryNode(@"C:\lib\Include");

// Link model to resource with metadata
var edge = new ResourceEdge
{
    ModelId = "model1",
    ResourceNodeId = resFile.Id,
    RawPath = "modelica://MyLib/Resources/data.mat",
    ReferenceType = ResourceReferenceType.LoadResource
};
graph.AddModelReferencesResource("model1", resFile.Id, edge);

// Query resource relationships
var edges = graph.GetResourceEdgesForModel("model1");
var resources = graph.GetResourcesForModel("model1");
var modelEdges = graph.GetModelEdgesToResource(resFile.Id);

// Cleanup orphaned resource nodes
graph.CleanupOrphanedResourceNodes();
```

### Style Checking

```csharp
using ModelicaGraph;

var settings = new StyleCheckingSettings
{
    ApplyFormattingRules = true,
    ImportStatementsFirst = true,
    OneOfEachSection = true,
    AnnotationAtEnd = true,
    ClassHasDescription = true,
    ParameterHasDescription = true,
    SpellCheckLanguages = ["en_US"],
    ValidateModelReferences = true
};

// Run style checks on a model definition
var violations = await StyleChecking.RunStyleCheckingAsync(
    modelDefinition, settings, basePackage: "MyLibrary");

// Run style checks on a model excluded from formatting
// When isExcludedFromFormatting is true, formatting-related rules are skipped:
// ImportStatementsFirst, InitialEQAlgoFirst/Last, OneOfEachSection,
// DontMixEquationAndAlgorithm, DontMixConnections
var violations = await StyleChecking.RunStyleCheckingAsync(
    modelDefinition, settings, basePackage: "MyLibrary",
    isExcludedFromFormatting: true);

foreach (var violation in violations)
    Console.WriteLine($"{violation.ModelName}: {violation.Summary}");
```

### Traversing Dependencies

```csharp
var tree = graph.GetDependencyTree("model1");

void PrintTree(TreeNode<IGraphNode> node, int indent = 0)
{
    Console.WriteLine(new string(' ', indent * 2) + node.Value.Name);
    foreach (var child in node.Children)
        PrintTree(child, indent + 1);
}

PrintTree(tree);
```

### Building a Graph Manually

```csharp
var graph = new DirectedGraph();

// Add file and models
var file = new FileNode("f1", "Models.mo");
var baseModel = new ModelNode("m1", "BaseModel");
var derivedModel = new ModelNode("m2", "DerivedModel");
graph.AddNode(file);
graph.AddNode(baseModel);
graph.AddNode(derivedModel);

// Create relationships
graph.AddFileContainsModel("f1", "m1");
graph.AddFileContainsModel("f1", "m2");
graph.AddModelUsesModel("m2", "m1");  // DerivedModel uses BaseModel

// Query
var deps = graph.GetUsedModels("m2");       // Returns [BaseModel]
var users = graph.GetModelUsedBy("m1");     // Returns [DerivedModel]
var models = graph.GetModelsInFile("f1");   // Returns [BaseModel, DerivedModel]
```

### StyleCheckingSettings Properties

Beyond the individual style rule toggles (e.g., `ImportStatementsFirst`, `AnnotationAtEnd`), `StyleCheckingSettings` includes these additional properties:

| Property | Type | Description |
|----------|------|-------------|
| `FormattingExcludedModels` | `List<string>` | Model IDs excluded from formatting. Use the helper method `IsModelExcludedFromFormatting(string modelId)` to check membership. |
| `SvnBranchDirectories` | `List<string>` | Configurable SVN branch directory names. Defaults to `["trunk", "branches", "tags"]`. |
| `HasAnyStyleRuleEnabled` | `bool` (computed) | Returns `true` if any style checking rule is enabled. |
| `SpellCheckLanguages` | `List<string>` | Language codes for spell checking dictionaries (e.g., `"en_US"`). |
| `ValidateModelReferences` | `bool` | Whether to validate `modelica://` model references. |

## Architecture

### Class Hierarchy

```
IGraphNode (interface)
    Id, NodeType, Name, Properties

GraphNode (abstract base)
    ├── FileNode         - FilePath, FileName, Content, ContainedModelIds
    ├── ModelNode        - Definition, ContainingFileId, UsedModelIds, UsedByModelIds
    ├── ResourceFileNode - ResolvedPath, FileExists, IsImageFile, ReferencedByModelIds
    └── ResourceDirectoryNode - ResolvedPath, DirectoryExists, ContainedFileIds

DirectedGraph           - Node/edge management, relationship queries
GraphBuilder (static)   - File loading, dependency analysis, model queries
StyleChecking (static)  - Style rule execution
StyleCheckingSettings   - Configurable rule toggles and additional settings
ModelDefinition         - Name, ModelicaCode, ParsedCode
ResourceEdge            - ModelId, ResourceNodeId, RawPath, ReferenceType
TreeNode<T>             - Generic tree structure for traversals
LibraryInfo             - Library metadata (name, path, root package)
```

### Node Properties

Each node has a `Properties` dictionary for storing additional metadata:

```csharp
modelNode.Properties["ClassType"] = "model";
modelNode.Properties["LoadSelectorParameters"] = parameterList;
fileNode.Properties["LastModified"] = DateTime.Now;
```

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Dependencies

- **ModelicaParser** - ANTLR-based parser used by GraphBuilder for parsing and analysis
