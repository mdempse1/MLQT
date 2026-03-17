# ModelicaParser

A .NET 10 parser for the Modelica language built on ANTLR 4, with code formatting, icon extraction, style checking, and external resource analysis.

## Overview

ModelicaParser uses ANTLR to generate a C# parser from the Modelica grammar file (`modelica.g4`). It provides:

- **Parsing** - Parse Modelica source code into an Abstract Syntax Tree (AST)
- **Model Extraction** - Extract model definitions with metadata (name, class type, line numbers, nesting)
- **Code Formatting** - Generate formatted Modelica source from parse trees with configurable rules
- **Icon Extraction** - Parse Icon annotations and render them as SVG
- **Style Checking** - Validate Modelica code against configurable style rules
- **Spell Checking** - Hunspell-based spell checking for description strings and documentation annotations
- **External Resource Extraction** - Detect references to external files and libraries

## Key Concepts

### Generated Parser

The build process automatically generates parser files from `modelica.g4`:

- `modelicaLexer.cs` - Tokenizes Modelica source code
- `modelicaParser.cs` - Parses tokens into an AST
- `modelicaListener.cs` / `modelicaBaseListener.cs` - Listener pattern for tree traversal
- `modelicaVisitor.cs` / `modelicaBaseVisitor.cs` - Visitor pattern for tree traversal

### Visitor-Based Architecture

All analysis is implemented as visitors over the parse tree:

| Visitor | Purpose |
|---------|---------|
| `ModelExtractorVisitor` | Extracts model definitions from source |
| `ModelicaRenderer` | Generates formatted Modelica code |
| `IconExtractor` | Extracts Icon annotation graphics |
| `ExternalResourceExtractor` | Detects external resource references |
| Style rule visitors | Check code against style guidelines |

## Usage

### Basic Parsing

```csharp
using ModelicaParser;

// Parse from string
var parseTree = ModelicaParserHelper.Parse(@"
    model SimpleModel
        Real x;
    equation
        der(x) = -x;
    end SimpleModel;
");

// Parse with error collection
var (tree, errors) = ModelicaParserHelper.ParseWithErrors(modelicaCode);
foreach (var error in errors)
    Console.WriteLine($"Line {error.Line}: {error.Message}");

// Parse with token stream (for comment-preserving operations)
var (tree, tokenStream) = ModelicaParserHelper.ParseWithTokens(modelicaCode);
```

### Extracting Model Definitions

```csharp
using ModelicaParser;

// Extract all models from source code
var models = ModelicaParserHelper.ExtractModels(modelicaCode);

foreach (var model in models)
{
    Console.WriteLine($"Found {model.ClassType}: {model.Name}");
    Console.WriteLine($"  Lines: {model.StartLine}-{model.StopLine}");
    Console.WriteLine($"  Can be standalone: {model.CanBeStoredStandalone}");

    if (model.IsNested)
        Console.WriteLine($"  Nested in: {model.ParentModelName}");
}
```

The `ModelInfo` class provides:

| Property | Type | Description |
|----------|------|-------------|
| `Name` | `string` | Model identifier |
| `SourceCode` | `string` | Full Modelica source code |
| `ClassType` | `string` | model, block, function, record, type, connector, package, class |
| `StartLine` / `StopLine` | `int` | Line number range in source file |
| `IsNested` | `bool` | Whether the model is contained within another model |
| `ParentModelName` | `string?` | Parent model name if nested |
| `CanBeStoredStandalone` | `bool` | Whether the model can be stored as a separate file |

### Code Formatting with ModelicaRenderer

```csharp
using ModelicaParser;

var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(modelicaCode);

// Basic formatting
var renderer = new ModelicaRenderer(tokenStream: tokenStream);
renderer.Visit(parseTree);
string formattedCode = string.Join("\n", renderer.Code);

// Formatting with options
var renderer = new ModelicaRenderer(
    renderForCodeEditor: true,       // Add markup for syntax highlighting
    showAnnotations: true,           // Include annotations in output
    maxLineLength: 100,              // Wrap lines exceeding this length
    importsFirst: true,              // Sort import statements first
    componentsBeforeClasses: true,   // Sort components before nested classes
    oneOfEachSection: true,          // Merge duplicate equation/algorithm sections
    tokenStream: tokenStream         // Preserve comments
);
renderer.Visit(parseTree);

// Exclude specific classes from output
var classesToExclude = new HashSet<string> { "InternalHelper", "PrivateImpl" };
var renderer = new ModelicaRenderer(
    excludeClassDefinitions: true,
    classNamesToExclude: classesToExclude,
    tokenStream: tokenStream
);
renderer.Visit(parseTree);
```

### Icon Extraction and SVG Rendering

```csharp
using ModelicaParser;

string modelicaCode = @"
model MyModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={255,255,255},
              fillPattern=FillPattern.Solid),
    Ellipse(extent={{-50,-50},{50,50}}, lineColor={0,0,255})
  }));
end MyModel;";

// Two-step process for more control
IconData? iconData = IconExtractor.ExtractIcon(modelicaCode);
if (iconData?.HasGraphics == true)
{
    string? svg = IconSvgRenderer.RenderToSvg(iconData, size: 32);
}

// With inheritance support (base class icons as background layer)
string? svg = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
    modelicaCode,
    baseClassName => LookupBaseClassCode(baseClassName),  // Resolver function
    size: 24,
    maxDepth: 10  // Prevents infinite loops in circular inheritance
);
```

Supported graphics primitives: `Rectangle`, `Ellipse`, `Line`, `Polygon`, `Text`, `Bitmap`.

### External Resource Extraction

```csharp
using ModelicaParser;

var parseTree = ModelicaParserHelper.Parse(modelicaCode);
var extractor = new ExternalResourceExtractor();
extractor.Visit(parseTree);

foreach (var resource in extractor.Resources)
{
    Console.WriteLine($"Path: {resource.RawPath}");
    Console.WriteLine($"Type: {resource.ReferenceType}");
    if (resource.ParameterName != null)
        Console.WriteLine($"Parameter: {resource.ParameterName}");
}
```

Detected reference types:

| Type | Source |
|------|--------|
| `LoadResource` | `Modelica.Utilities.Files.loadResource()` calls |
| `UriReference` | `modelica://` URIs in strings |
| `LoadSelector` | Parameters with `loadSelector` annotation |
| `LoadResourceParameter` | Modification of parameter with `loadResource()` default |
| `ExternalInclude` | `Include` annotation on external functions |
| `ExternalLibrary` | `Library` annotation on external functions |
| `ExternalIncludeDirectory` | `IncludeDirectory` annotation |
| `ExternalLibraryDirectory` | `LibraryDirectory` annotation |
| `ExternalSourceDirectory` | `SourceDirectory` annotation |

### Style Rule Checking

All style rules extend `VisitorWithModelNameTracking` and populate a `RuleViolations` list:

```csharp
using ModelicaParser;

var parseTree = ModelicaParserHelper.Parse(modelicaCode);

// Check that annotations are at the end of class definitions
var rule = new AnnotationAtEnd(basePackage: "MyLibrary");
rule.Visit(parseTree);

foreach (var violation in rule.RuleViolations)
    Console.WriteLine($"{violation.ModelName} line {violation.LineNumber}: {violation.Summary}");
```

#### VisitorWithModelNameTracking and Nested Class Skipping

All style rule visitors extend `VisitorWithModelNameTracking`, which provides model name context and enforces a nested-class skip policy:

- Style rule visitors only check the **outermost class definition** in the parse tree.
- Nested class definitions are skipped when the depth exceeds 1, because each nested class has its own `ModelNode` in the graph and is checked independently.
- This prevents duplicate violations when a parent package's code includes nested class source code.
- The `_classDepth` counter tracks nesting level; only depth == 1 (the first `class_definition` encountered) is visited. Deeper definitions return immediately without invoking rule logic.

Available style rules:

| Rule | Description |
|------|-------------|
| `AnnotationAtEnd` | Class annotation must be the last element |
| `CheckClassDescriptionStrings` | Classes must have description strings |
| `ExtendsClausesAtTop` | Extends clauses should appear at the top |
| `ImportStatementsFirst` | Import statements should come first |
| `InitialEquationFirst` | Initial equation/algorithm sections should come first |
| `MixConnectionsAndEquations` | Don't mix connect() calls with equations |
| `OneOfEachSection` | Only one of each section type (public, protected, equation, etc.) |
| `PublicParametersAndConstantsHaveDescription` | Public parameters and constants need descriptions |
| `FollowNamingConvention` | Checks class/element names against configurable naming conventions |
| `SpellCheckDescriptions` | Spell checks description strings on classes and components |
| `SpellCheckDocumentation` | Spell checks Documentation annotation HTML content |

### Spell Checking

The spell checking system uses WeCantSpell.Hunspell with embedded English dictionaries and supports additional languages:

```csharp
using ModelicaParser.SpellChecking;

// Create a spell checker with default dictionaries
var checker = SpellChecker.Create();

// Check a word
bool isCorrect = checker.IsCorrect("temperature");

// Check with context words (e.g., component names that are valid in scope)
var contextWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "rflx", "pOut" };
bool isCorrect = checker.IsCorrect("rflx", contextWords);

// Get spelling suggestions
IReadOnlyList<string> suggestions = checker.Suggest("temperture");

// Add a custom word at runtime
checker.AddCustomWord("Dymola");

// Extract text from HTML documentation for checking
string plainText = TextExtractor.StripHtml("<html><p>Check this text.</p></html>");

// Tokenize into words with character offsets
foreach (var (word, offset) in TextExtractor.TokenizeToWords(plainText))
{
    if (!TextExtractor.ShouldSkipWord(word) && !checker.IsCorrect(word))
        Console.WriteLine($"Misspelled: {word} at offset {offset}");
}
```

### Using the Visitor Pattern

```csharp
using Antlr4.Runtime.Tree;

public class MyModelicaVisitor : modelicaBaseVisitor<object>
{
    public override object VisitClass_definition(modelicaParser.Class_definitionContext context)
    {
        // Custom logic for visiting class definitions
        return base.VisitClass_definition(context);
    }
}

var parseTree = ModelicaParserHelper.Parse(modelicaCode);
var visitor = new MyModelicaVisitor();
visitor.Visit(parseTree);
```

### Using the Listener Pattern

```csharp
using Antlr4.Runtime.Tree;

public class MyModelicaListener : modelicaBaseListener
{
    public override void EnterClass_definition(modelicaParser.Class_definitionContext context)
    {
        base.EnterClass_definition(context);
    }
}

var parseTree = ModelicaParserHelper.Parse(modelicaCode);
var walker = new ParseTreeWalker();
var listener = new MyModelicaListener();
walker.Walk(listener, parseTree);
```

## Building

The parser is automatically regenerated from the grammar file during build:

```bash
dotnet build ModelicaParser.csproj
```

## Dependencies

- **Antlr4.Runtime.Standard** (4.13.1) - ANTLR runtime for C#
- **Antlr4BuildTasks** (12.14.0) - MSBuild tasks for generating parser code from grammar files
- **WeCantSpell.Hunspell** (7.0.1) - Managed .NET Hunspell port for spell checking

## License

MIT License — see [LICENSE](../LICENSE) for details.

## Grammar

The grammar file (`modelica.g4`) is based on the Modelica language specification and is licensed under the BSD license. See the file header for complete license information.
