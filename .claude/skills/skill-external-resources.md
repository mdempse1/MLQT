# External Resources Skill

This skill covers the external resource tracking system, including extraction, resolution, graph nodes, and the External Resources UI page.

## Overview

External resources in Modelica include data files, C/C++ headers, compiled libraries, images, and documentation files. The system tracks these as proper graph nodes with edges linking them to referencing models.

## Resource Reference Types

| Type | Source | Example |
|------|--------|---------|
| `LoadResource` | `Modelica.Utilities.Files.loadResource()` calls | `loadResource("modelica://Lib/data.mat")` |
| `UriReference` | `modelica://` URIs in documentation, Bitmap | `<img src="modelica://Lib/icon.png">` |
| `LoadSelector` | Parameters with `loadSelector` annotation | `parameter String fileName annotation(Dialog(loadSelector(...)))` |
| `LoadResourceParameter` | Modification of parameter with `loadResource()` default | See note below |
| `ExternalInclude` | `Include` annotation on external functions | `annotation(Include="#include \"header.h\"")` |
| `ExternalLibrary` | `Library` annotation on external functions | `annotation(Library="mylib")` |
| `ExternalIncludeDirectory` | `IncludeDirectory` annotation | `annotation(IncludeDirectory="modelica://Lib/Include")` |
| `ExternalLibraryDirectory` | `LibraryDirectory` annotation | `annotation(LibraryDirectory="modelica://Lib/Library")` |
| `ExternalSourceDirectory` | `SourceDirectory` annotation | `annotation(SourceDirectory="modelica://Lib/Source")` |

### LoadResourceParameter Detection

When a parameter uses `loadResource()` for its default value:

```modelica
model DataLoader
  parameter String fileName = Modelica.Utilities.Files.loadResource("modelica://Lib/default.mat");
end DataLoader;
```

And another model modifies that parameter WITHOUT using loadResource:

```modelica
model UserModel
  DataLoader loader(fileName="modelica://MyLib/data.mat");  // Detected!
end UserModel;
```

This modification is detected as a `LoadResourceParameter` reference. The system uses a two-pass approach:
1. **Pass 1**: Identifies parameters with `loadResource()` defaults, stores in `ModelNode.Properties["LoadResourceParameters"]`
2. **Pass 2**: Detects modifications of these parameters across all models in the graph

## Graph Node Types

### ResourceFileNode

Represents a file resource (data files, headers, library binaries, images).

```csharp
public class ResourceFileNode : GraphNode
{
    public string ResolvedPath { get; }      // Absolute file system path
    public bool FileExists { get; set; }      // Whether file exists on disk
    public bool IsImageFile { get; set; }     // True for image files
    public List<string> ReferencedByModelIds { get; }  // Models referencing this file
}
```

### ResourceDirectoryNode

Represents a directory resource (IncludeDirectory, LibraryDirectory, SourceDirectory).

```csharp
public class ResourceDirectoryNode : GraphNode
{
    public string ResolvedPath { get; }       // Absolute directory path
    public bool DirectoryExists { get; set; } // Whether directory exists
    public List<string> ContainedFileIds { get; } // Files scanned in directory
    public List<string> ReferencedByModelIds { get; }
}
```

### ResourceEdge

Edge metadata linking models to resources.

```csharp
public class ResourceEdge
{
    public string ModelId { get; set; }
    public string ResourceNodeId { get; set; }
    public string RawPath { get; set; }       // Original path from source
    public ResourceReferenceType ReferenceType { get; set; }
    public string? ParameterName { get; set; } // For LoadSelector references
    public bool IsAbsolutePath { get; set; }   // Non-portable warning
}
```

## Extraction Pipeline

### 1. ExternalResourceExtractor (ModelicaParser)

Visits the parse tree to extract raw resource references.

```csharp
var extractor = new ExternalResourceExtractor();
extractor.Visit(parseTree);
List<ExternalResourceInfo> resources = extractor.Resources;
```

**Extracted Sources:**
- `loadResource()` function calls
- `modelica://` URIs in strings
- External function annotations (Include, Library, directories)
- Bitmap fileName attributes

### 2. LoadSelectorAnalyzer (ModelicaGraph)

Detects parameters with `loadSelector` annotations and tracks modifications.

```csharp
var analyzer = new LoadSelectorAnalyzer(modelId, graph);
analyzer.Visit(parseTree);
// Registers loadSelector parameters on ModelNode.Properties["LoadSelectorParameters"]
// Detects when components modify loadSelector parameters with file paths
```

### 3. GraphBuilder.ExtractExternalResources

Resolves raw paths and creates graph nodes.

**Resolution Steps:**
1. Parse `modelica://` URIs using library root paths
2. Resolve relative paths against model file location
3. For `ExternalInclude`: Parse `#include "header.h"` and resolve using IncludeDirectory
4. For `ExternalLibrary`: Search platform directories (win64, linux64, etc.)
5. Create ResourceFileNode or ResourceDirectoryNode
6. Create ResourceEdge linking model to resource

### 4. ExternalResourceService (MLQT.Services)

Reads from graph, generates warnings, monitors files.

```csharp
await ExternalResourceService.AnalyzeResourcesAsync(graph);

// Get all resources
List<ExternalResourceReference> allResources = ExternalResourceService.GetAllResources();

// Get resources for a model
List<ExternalResourceReference> modelResources = ExternalResourceService.GetResourcesForModel(modelId);

// Get models referencing a resource
List<string> modelIds = ExternalResourceService.GetModelsReferencingResource(resolvedPath);

// Get warnings
List<ResourceWarning> warnings = ExternalResourceService.GetWarnings();
```

## Path Resolution

### Modelica URI Resolution

```
modelica://LibraryName/Resources/data.mat
         ↓
C:\Libraries\LibraryName\Resources\data.mat
```

### Include Directive Parsing

The `#include` directive is parsed from ANTLR tokens which preserve escape sequences:

```csharp
// Raw from ANTLR: #include \"ModelicaStandardTables.h\"
// After unescaping: #include "ModelicaStandardTables.h"
// Extracted filename: ModelicaStandardTables.h
```

### Library Resolution

External libraries are searched in platform-specific directories:

```
Resources/Library/
├── win64/
│   ├── vs2022/
│   │   └── MyLib.lib
│   └── MyLib.lib
├── linux64/
│   └── libMyLib.a
└── MyLib.lib (fallback)
```

Search order:
1. `{LibraryDir}/{platform}/{compiler}/{lib}`
2. `{LibraryDir}/{platform}/{lib}`
3. `{LibraryDir}/{lib}`

Platform extensions:
- Windows: `.lib`, `.dll`
- Unix: `.a`, `.so`

## External Resources Page

**Location**: `MLQT.Shared/Pages/ExternalResources.razor`

### Features

1. **Tree View**: Resources organized by directory structure
2. **File Type Filtering**: Data, C/C++, Libs, Images, Documentation
3. **Warning Indicators**: Missing files, absolute paths
4. **Reference Details**: Click to see which models reference a resource

### Directory Annotation Icons

Annotated directories have distinct icons and colors:

| Annotation Type | Icon | Color |
|-----------------|------|-------|
| IncludeDirectory | Code | Primary (Blue) |
| LibraryDirectory | LibraryBooks | Secondary (Purple) |
| SourceDirectory | DataObject | Tertiary (Teal) |
| Regular folder | Folder | Warning (Yellow) |

### Clicking Behavior

- **Files**: Shows list of models referencing the file
- **Annotated directories**: Shows models that use this directory annotation
- **Regular directories**: No action (intermediate path nodes)

### ResourceTreeNode Data Model

```csharp
public class ResourceTreeNode
{
    public string Name { get; set; }
    public string FullPath { get; set; }
    public bool IsDirectory { get; set; }
    public DirectoryAnnotationType AnnotationType { get; set; }
    public List<string> ReferencingModelIds { get; set; }
    public bool HasWarning { get; set; }
    public string? WarningMessage { get; set; }
    public string FileExtension { get; set; }
    public bool IsImageFile { get; set; }
    public int ReferencingModelCount { get; set; }
}

public enum DirectoryAnnotationType
{
    None,
    IncludeDirectory,
    LibraryDirectory,
    SourceDirectory
}
```

## Key Files

| File | Purpose |
|------|---------|
| `ModelicaParser/ExternalResourceInfo.cs` | Resource info and ResourceReferenceType enum |
| `ModelicaParser/ExternalResourceExtractor.cs` | Parse tree visitor for extraction |
| `ModelicaGraph/ResourceFileNode.cs` | File resource graph node |
| `ModelicaGraph/ResourceDirectoryNode.cs` | Directory resource graph node |
| `ModelicaGraph/ResourceEdge.cs` | Edge metadata |
| `ModelicaGraph/GraphBuilder.cs` | Resolution and node creation |
| `ModelicaGraph/LoadSelectorAnalyzer.cs` | LoadSelector parameter detection |
| `MLQT.Services/Interfaces/IExternalResourceService.cs` | Service interface |
| `MLQT.Services/ExternalResourceService.cs` | Service implementation |
| `MLQT.Services/DataTypes/ResourceTreeNode.cs` | UI tree node model |
| `MLQT.Shared/Pages/ExternalResources.razor` | UI page |

## Tests

```bash
dotnet test ModelicaParser.Tests --filter ExternalResourceExtractor
dotnet test ModelicaGraph.Tests --filter LoadSelectorAnalyzer
dotnet test ModelicaGraph.Tests --filter "AnalyzeDependencies_WithExternal"
```

Key test files:
- `ModelicaParser.Tests/ExternalResourceExtractorTests.cs` - 36 tests
- `ModelicaGraph.Tests/LoadSelectorAnalyzerTests.cs` - 20 tests
- `ModelicaGraph.Tests/GraphBuilderTests.cs` - External resource resolution tests
