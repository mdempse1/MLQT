---
name: external-resources
description: "External resource tracking - extraction from loadResource, modelica:// URIs and external annotations, resolution, the ResourceFileNode/ResourceDirectoryNode graph nodes and ResourceEdge, and the External Resources page. Use when working on resource analysis or that page."
---

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
1. **Pass 1** (`ModelAnalyzer`): identifies parameters with `loadSelector` annotations or `loadResource()`
   defaults and stores their names in `ModelNode.LoadSelectorParameters` / `ModelNode.LoadResourceParameters`
2. **Pass 2** (`LoadSelectorModificationAnalyzer`, phase 3 of `GraphBuilder.AnalyzeDependenciesAsync`):
   detects modifications of these parameters — only over the models `MayModifyATrackedParameter`
   selects, never the whole graph again (B282)

## Graph Node Types

### ResourceFileNode

Represents a file resource (data files, headers, library binaries, images).

```csharp
public class ResourceFileNode : GraphNode
{
    public string ResolvedPath { get; }      // Absolute file system path
    public bool FileExists { get; set; }      // Whether file exists on disk
    public bool IsImageFile { get; set; }     // True for image files
    public HashSet<string> ReferencedByModelIds { get; }  // Models referencing this file
}
```

### ResourceDirectoryNode

Represents a directory resource (IncludeDirectory, LibraryDirectory, SourceDirectory).

```csharp
public class ResourceDirectoryNode : GraphNode
{
    public string ResolvedPath { get; }       // Absolute directory path
    public bool DirectoryExists { get; set; } // Whether directory exists
    public HashSet<string> ContainedFileIds { get; } // Files scanned in directory
    public HashSet<string> ReferencedByModelIds { get; }
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

### 1. Extraction: ExternalResourceExtractor (ModelicaParser), inherited by ModelAnalyzer

**There is one rule set**, in `ModelicaParser/Visitors/ExternalResourceExtractor.cs`: the
loadResource capture (`IsLoadResourceCall`, `ResourceArgument.SoleStringLiteral`), the
`modelica://` scan (`ModelicaUriScanner`) and the external clause's annotation keys. The graph
build's `ModelicaGraph/ParserVisitors/ModelAnalyzer.cs` **derives from it**, so the visit that finds
dependencies finds resources by exactly those rules. On a parse tree outside the graph, use the
extractor itself:

```csharp
var extractor = new ExternalResourceExtractor();
extractor.Visit(parseTree);
List<ExternalResourceInfo> resources = extractor.Resources;
```

What `ModelAnalyzer` adds is only what needs the graph: dependencies, and pass 1 of the loadSelector
tracking — it flags a parameter whose default is a `loadResource` call (the short, imported form
`loadResource(...)` included, which is never captured as a resource) and adds `LoadSelector` entries
to the same `Resources` list. An override in it calls `base.VisitPrimary`, so the extractor's rules
still run.

**A new resource rule goes in the extractor, and nowhere else.** Until B511 `ModelAnalyzer` held a
copy of every rule, and only tests constructed the extractor: B209 was fixed in the extractor, its
tests went green, and the graph build went on with the copy until the URI scan moved into
`ModelicaUriScanner`. `ModelicaGraph.Tests/ResourceRuleParityTests.cs` now reads every snippet in
`ExternalResourceExtractorTests.cs` and asserts the production visitor finds the same resources, and
fails if a resource rule reappears in `ModelAnalyzer.cs` — so a case added to the extractor's tests is
checked against the graph build without a second edit. Keep those tests' snippets in the
`var code = @"...";` form the parity test reads.

**Extracted Sources:**
- `loadResource()` function calls
- `modelica://` URIs in strings
- External function annotations (Include, Library, directories)
- Bitmap fileName attributes

### 2. LoadSelectorModificationAnalyzer (ModelicaGraph)

Pass 2: finds component modifications of the `loadSelector`/`loadResource` parameters pass 1 found.

```csharp
var index = LoadSelectorModificationAnalyzer.ParameterIndex.Build(graph);   // once per pass
var analyzer = new LoadSelectorModificationAnalyzer(modelId, graph, index);
analyzer.Visit(parseTree);
List<ExternalResourceInfo> found = analyzer.Resources;
```

Build the `ParameterIndex` once: it describes the graph, not the model, and building it per model
made the pass cost models × graph size.

### 3. GraphBuilder resolution (`CreateResourceNodeAndEdge`, `ResolveResourcePath`)

Resolves raw paths and creates graph nodes, inside `AnalyzeDependenciesAsync` /
`AnalyzeDependenciesForModelsAsync`.

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
await ExternalResourceService.AnalyzeResourcesForModelsAsync(modelIds, graph);   // incremental

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

`GraphBuilder.ResolveAllLibraryFiles` collects **every** platform variant that exists, not the first
hit. For each platform directory (`win32`, `win64`, `linux32`, `linux64`, `darwin32`, `darwin64`) it
searches:
1. `{LibraryDir}/{platform}/{compiler}/{lib}` — compilers `vs2010`…`vs2022`, `gcc`, `gcc47`–`gcc49`, `clang`
2. `{LibraryDir}/{platform}/{lib}`

and then `{LibraryDir}/{lib}`.

Platform extensions:
- Windows (`win32`/`win64`): `.lib`, `.dll`, `.a`
- Linux: `.a`, `.so`
- macOS (`darwin*`): `.a`, `.dylib`

## External Resources Page

**Location**: `MLQT.Shared/Pages/ExternalResources.razor` (+ `.razor.cs`)

### Features

1. **Tree View**: Resources organized by directory structure
2. **File Type Filtering**: Data Files, C/C++, Libs, Images, Documentation, All
3. **Warning Filtering and Indicators**: Missing files, absolute paths
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
    public bool IsMissing { get; set; }
    public bool IsAbsolutePath { get; set; }
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
| `ModelicaParser/DataTypes/ExternalResourceInfo.cs` | Resource info and ResourceReferenceType enum |
| `ModelicaParser/Visitors/ExternalResourceExtractor.cs` | The one rule set for extraction; usable alone on a parse tree |
| `ModelicaGraph/ParserVisitors/ModelAnalyzer.cs` | The graph build's visitor: derives from the extractor, adds dependencies and loadSelector pass 1 |
| `ModelicaGraph/ParserVisitors/LoadSelectorModificationAnalyzer.cs` | Pass 2: modifications of loadSelector/loadResource parameters |
| `ModelicaGraph/DataTypes/ResourceFileNode.cs` | File resource graph node |
| `ModelicaGraph/DataTypes/ResourceDirectoryNode.cs` | Directory resource graph node |
| `ModelicaGraph/DataTypes/ResourceEdge.cs` | Edge metadata |
| `ModelicaGraph/GraphBuilder.cs` | Resolution and node creation |
| `MLQT.Services/Interfaces/IExternalResourceService.cs` | Service interface |
| `MLQT.Services/ExternalResourceService.cs` | Service implementation, warnings, file watchers |
| `MLQT.Services/DataTypes/ExternalResourceReference.cs` | What the service returns per reference |
| `MLQT.Services/DataTypes/ResourceTreeNode.cs` | UI tree node model |
| `MLQT.Shared/Pages/ExternalResources.razor` (+ `.razor.cs`) | UI page |

## Tests

```bash
dotnet test ModelicaParser.Tests --filter ExternalResourceExtractor
dotnet test ModelicaGraph.Tests --filter LoadSelector
dotnet test ModelicaGraph.Tests --filter ResourceRuleParity
dotnet test ModelicaGraph.Tests --filter "AnalyzeDependencies_WithExternal"
dotnet test MLQT.Services.Tests --filter ExternalResourceService
```

Key test files:
- `ModelicaParser.Tests/ExternalResourceExtractorTests.cs` (and `ExternalResourceEntityTests.cs`)
- `ModelicaGraph.Tests/ResourceRuleParityTests.cs` - the graph build finds what the extractor finds (B511)
- `ModelicaGraph.Tests/LoadSelectorAnalyzerTests.cs` and `LoadSelectorPassTwoScopeTests.cs`
- `ModelicaGraph.Tests/GraphBuilderTests.cs` - External resource resolution tests (`AnalyzeDependencies_WithExternal*`)
- `MLQT.Services.Tests/ExternalResourceServiceTests.cs`
- `MLQT.Shared.Tests/Pages/ExternalResourceCategoryTests.cs` - the page's file-type categories
