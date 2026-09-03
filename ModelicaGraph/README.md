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

When a file contains code the parser cannot process — for example Modelica that Dymola accepts but the grammar rejects — `LoadModelicaFile` creates a placeholder `ModelNode` carrying the full file contents and marks it with `IsParseFailurePlaceholder = true`. A `ParserError` with `Severity = ParserErrorSeverity.FatalParseFailure` is attached so the UI can distinguish fatal failures from recovered syntax errors. Downstream analysis (dependency analysis, style checking, formatting) skips placeholder nodes.

### StyleChecking

`StyleChecking` provides a static method to run configurable style rules against model definitions, with `StyleCheckingSettings` controlling which rules are active.

## Usage

### Creating and Populating a Graph

```csharp
using ModelicaGraph;

var graph = new DirectedGraph();

// Load a Modelica file — the path identifies the file, its content is parsed for models.
// (The content is not stored on the graph; re-read it from disk when needed.)
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

// Dependency queries (by fully-qualified model id)
var dependencies = graph.GetUsedModels("MyLibrary.MyModel"); // models this one depends on
var dependents = graph.GetModelUsedBy("MyLibrary.MyModel");  // models that depend on this one

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
    ComponentsBeforeClasses = true,
    ClassHasDescription = true,
    ParameterHasDescription = true,
    SpellCheckLanguages = ["en_US"],
    ValidateModelReferences = true
};

// Run style checks on a model definition. RunStyleChecking is synchronous and
// returns a List<LogMessage>; the model is identified by its fullModelId.
List<LogMessage> findings = StyleChecking.RunStyleChecking(
    modelDefinition, settings, fullModelId: "MyLibrary.MyModel");

// Run style checks on a model excluded from formatting
// When isExcludedFromFormatting is true, formatting-related rules are skipped:
// ImportStatementsFirst, InitialEQAlgoFirst/Last, OneOfEachSection,
// DontMixEquationAndAlgorithm, DontMixConnections
findings = StyleChecking.RunStyleChecking(
    modelDefinition, settings, fullModelId: "MyLibrary.MyModel",
    isExcludedFromFormatting: true);

foreach (var finding in findings)
    Console.WriteLine($"{finding.ModelName}: {finding.Summary}");
```

### Traversing Dependencies

Walk the dependency relationships directly with the graph's query methods (there is no
pre-built tree object — recurse over `GetUsedModels` / `GetModelUsedBy` yourself):

```csharp
void PrintDependencies(string modelId, int indent = 0, HashSet<string>? seen = null)
{
    seen ??= new HashSet<string>();
    if (!seen.Add(modelId)) return; // guard against cycles
    Console.WriteLine(new string(' ', indent * 2) + modelId);
    foreach (var dep in graph.GetUsedModels(modelId))
        PrintDependencies(dep.Id, indent + 1, seen);
}

PrintDependencies("MyLibrary.MyModel");
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
GraphBuilder (static)   - File loading and dependency analysis (model queries live on DirectedGraph)
ExternalStubBuilder     - Nodes for encrypted libraries, from vendor documentation
StyleChecking (static)  - Style rule execution, and the base-class icon / inherited-element lookups
StyleCheckingSettings   - Rule severities, formatter flags, naming, spell-check languages
GraphAnalysisRunner     - The whole-graph analyses (see below)
MetricsCalculator       - Coverage by dimension, and the snapshots behind the burndown
ModelDefinition         - Name, ModelicaCode, ParsedCode
ResourceEdge            - ModelId, ResourceNodeId, RawPath, ReferenceType
LibraryInfo             - Library metadata (name, path, root package)
```

### Whole-graph analyses (`Analysis/`)

The style rules judge a class from its own source. These judge it from its place in the graph — a
question no single class can answer — and run per repository alongside the per-class checks, through
`GraphAnalysisRunner`.

| Analyzer | Answers |
|----------|---------|
| `UnusedImportAnalyzer` | An import nothing below it references (the referencing class is often another file, so this cannot be decided class by class) |
| `UnusedClassAnalyzer` | A protected nested class nothing references; separately, a public one nothing *loaded* references, at lower confidence |
| `UnusedMembersAnalyzer` | A protected member never referenced in its class — asked only where the answer is safe (nothing extends the class, and it has no nested classes that could reference the name) |
| `ShadowingAnalyzer` | A declaration that silently shadows an inherited member |
| `UsesHygieneAnalyzer` | A library referenced but not declared in `uses(...)`, or declared and never used |
| `PackageOrderAnalyzer` | `package.order` entries that do not match the package's contents |

They need dependency edges, so the runner arranges for `DirectedGraph.DependenciesAnalyzed` to be
true first; without the edges the edge-dependent ones are skipped rather than guessing.

Resolution shared with the analyses and the MCP tooling: `TypeResolver` (a type name → the class it
means, through imports and the package hierarchy), `ClassElementResolver` (a class's full element
set with inheritance merged in, derived declarations shadowing inherited ones) and `UnitResolver`
(whether a declared type carries a unit, through alias and SI type chains).

### Metrics and coverage (`Analysis/`)

`CoverageDimension` names what can be measured — class description, documentation info and
revisions, icon, parameter and constant description, unit, and the layout dimensions the formatter
can rewrite — and `CoverageDimensions.TrackedFor(settings)` narrows that to what a repository's own
rules ask for: a rule nobody enabled is not a gap anyone should be shown. `MetricsCalculator` and
`CoverageMeasurer` do the measuring. Measurement
happens while a class is being checked, since the parse tree is already in hand.

`MetricsSnapshot` is a point in the burndown: coverage by dimension plus the raw compliant/eligible
counts, so snapshots from several repositories combine exactly rather than by averaging percentages.
They are appended to `.mlqt/metrics-history.json` by the dashboard or by `mlqt check --metrics`.

### External Stubs

A library that ships encrypted (`package.moe`) has no readable source, so `ExternalStubBuilder`
builds its nodes from the classes `ModelicaParser.ExternalDocs` recovers from the vendor's
documentation. Each node's `Definition.ModelicaCode` is a **synthesized declaration** carrying only
what the documentation stated — name, description, `extends`, and whether there is an icon:

```modelica
within Battery.BMS.Interfaces;
model CurrentRestrictor "Interface model for current restrictor"
  extends DymolaModels.Icons.Templates.Box_Bottom;
  annotation (Icon(graphics={Rectangle(extent={{-100,-100},{100,100}})}));
end CurrentRestrictor;
```

Synthesizing source rather than carrying parallel metadata is what makes this cheap: every consumer
already works through the parse tree — icon inheritance, the type and element resolvers, dependency
analysis, reference validation — so a stub that parses is resolved by all of them with no rule
changes.

Such nodes are flagged `ModelNode.IsExternalStub`. That flag has one job: keep them off every path
that **writes** or **reports**. `ModelicaPackageSaver` throws rather than skipping (a caller holding
stubs has a bug, and it should surface in a test rather than as a rewritten third-party library),
`PackageCodeTrimmer` and `MetricsCalculator` skip them, and `LibraryCheckSession` filters them out
centrally so no surface can drift.

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
