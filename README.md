# MLQT — Modelica Library Quality Toolkit

MLQT is an open-source desktop application and set of libraries for managing, analyzing, and reviewing [Modelica](https://modelica.org/) libraries stored in version control systems (Git or SVN).

## Origin

MLQT started from a familiar frustration: every time a Modelica tool saved a file, it would introduce a flurry of whitespace and formatting changes that cluttered commits, obscured the real edits in diffs, and made code review painful. The original goal was simple — put a layer between Modelica tools and the repository that applied consistent formatting to every `.mo` file before it was committed, so that Git and SVN diffs showed meaningful changes rather than stylistic churn. Formatting rules are stored in the repository itself so everyone on the team applies the same rules.

From that starting point, MLQT grew into a broader set of tools for working with Modelica code. The same parser that powers the formatter also drives configurable style checking, dependency impact analysis, and external resource tracking, alongside integrations with Dymola and OpenModelica for model checking.

## What Is MLQT?

Modelica is an object-oriented language for modeling complex physical systems (mechanical, electrical, thermal, hydraulic, etc.). Large Modelica projects typically store their model libraries in Git or SVN and involve teams who need to:

- Review what has changed between revisions
- Understand which other models are affected when a model is modified
- Apply consistent formatting rules to your Modelica code
- Check naming conventions and coding style guidelines
- Check models with simulation tools (Dymola, OpenModelica) before committing
- Track external resources (data files, C libraries, images) referenced by models

MLQT provides a UI to do all of this as your primary way to interact with your version control system.

## Key Features

- **Library Browser** — Browse Modelica package hierarchies with syntax-highlighted code viewing
- **Version Control Integration** — Review uncommitted changes, view history, switch branches, commit, update, merge for both Git and SVN repositories
- **Impact Analysis** — Select a set of models and see the network of models that depend on them, visualized as an interactive graph
- **Style Checking** — Configurable rules enforce coding conventions (description strings, section ordering, naming conventions, etc.)
- **Spell Checking** — Hunspell-based spell checking for description strings and documentation annotations with multi-language support, custom dictionaries, and intelligent word filtering
- **External Resources** — Track all data files, C libraries, and images referenced by models; detect missing files and portability issues
- **Code Formatting** — Auto-format Modelica source with configurable rules (section ordering, imports first, annotation placement, etc.)
- **Dymola Integration** — Check and simulate models via Dymola's HTTP JSON-RPC interface
- **OpenModelica Integration** — Check and simulate models via OMC's ZeroMQ interface

## Project Structure

This repository contains the open-source components of MLQT:

| Project | Description |
|---------|-------------|
| [MLQT](MLQT/) | .NET MAUI application host — bootstraps the UI, DI, and platform services |
| [MLQT.Shared](MLQT.Shared/) | All Blazor UI: pages, components, layout, application state |
| [MLQT.Services](MLQT.Services/) | Business logic services: library management, repository integration, file monitoring, style checking, impact analysis |
| [ModelicaParser](ModelicaParser/) | ANTLR 4 parser for Modelica — parsing, formatting, icon extraction, style rules, resource extraction |
| [ModelicaGraph](ModelicaGraph/) | Directed graph of file/model/resource relationships and dependencies |
| [RevisionControl](RevisionControl/) | Unified Git and SVN interface with workspace management |
| [DymolaInterface](DymolaInterface/) | .NET client for Dymola's HTTP JSON-RPC API |
| [OpenModelicaInterface](OpenModelicaInterface/) | .NET client for OpenModelica Compiler (OMC) via ZeroMQ |

Each project has a README with detailed API documentation and user documentation is available in [Documentation](Documentation/) folder with a [Getting Started Guide](Documentation/getting-started.md)

## Requirements

- **.NET 10 SDK** — [Download](https://dotnet.microsoft.com/download/dotnet/10.0)
- **Windows 10/11** — The MAUI desktop application currently builds for Windows only
- **Git or SVN** — At least one VCS installed for repository operations
- **Dymola** (optional) — Dymola 2025x Refresh 1 or later for model checking
- **OpenModelica** (optional) — OpenModelica 1.24.0 or later for model checking

## Building

```bash
# Clone the repository
git clone <repository-url>
cd <repository-directory>

# Build all projects
dotnet build

# Run the application
dotnet run --project MLQT/MLQT.csproj
```

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests for a specific project
dotnet test ModelicaParser.Tests
dotnet test ModelicaGraph.Tests
dotnet test RevisionControl.Tests
dotnet test MLQT.Services.Tests
```

## Continuous Integration

GitHub Actions workflows run automatically on pushes to `main`/`develop` and on pull requests:

- **Build & Test** — Builds all library and test projects, runs all test suites, uploads test results as artifacts
- **Build MAUI App** — Verifies the Windows desktop application builds successfully
- **Code Coverage** — Runs tests with coverage collection and generates a summary report

### What isn't tested in CI

- **DymolaInterface.Tests** — Requires a licensed Dymola installation, which is not available on CI runners
- **OpenModelicaInterface.Tests** — Requires an OpenModelica installation, which is not available on CI runners
- **SVN integration tests** — Excluded via test filter (`FullyQualifiedName!~Svn`) because they require a local SVN repository and working copy

These tests should be run locally when making changes to the affected projects.

### Coverage targets

| Project | Target |
|---------|--------|
| **ModelicaParser** | >95% — this is the core parser and must be thoroughly tested |
| **All other projects** | >80% |

DymolaInterface and OpenModelicaInterface are excluded from CI coverage reports since they cannot be tested without their respective tools installed.

RevisionControl coverage will appear low in CI reports because the SVN integration tests are excluded (they require a local SVN repository). The full test suite, including SVN tests, should be run locally to verify actual coverage meets the >80% target.

## Architecture Overview

MLQT is built as a **Blazor application hosted inside .NET MAUI** using `BlazorWebView`. This gives a native desktop application with a web-based UI:

```
MLQT (MAUI host)
└── MLQT.Shared (Blazor UI — pages, components, layout)
    └── MLQT.Services (business logic, injectable services)
        ├── ModelicaGraph (dependency graph)
        │   └── ModelicaParser (ANTLR Modelica parser)
        ├── RevisionControl (Git/SVN)
        ├── DymolaInterface (Dymola HTTP client)
        └── OpenModelicaInterface (OMC ZeroMQ client)
```

All business logic lives in service classes with interfaces, registered as singletons in dependency injection. The UI communicates with services via events — no direct coupling between components.

## Using the Libraries Independently

The lower-level libraries (ModelicaParser, ModelicaGraph, RevisionControl) are designed to be used independently of the MLQT application:

```csharp
// Parse and analyze Modelica code
using ModelicaParser;
var models = ModelicaParserHelper.ExtractModels(modelicaCode);

// Build a dependency graph
using ModelicaGraph;
var graph = new DirectedGraph();
GraphBuilder.LoadModelicaDirectory(graph, "path/to/library");
await GraphBuilder.AnalyzeDependenciesAsync(graph, libraries);

// Work with Git/SVN
using RevisionControl;
var git = new GitRevisionControlSystem();
var changes = git.GetWorkingCopyChanges(@"C:\Projects\MyRepo");
```

See each project's README for full API documentation and examples.

## License

MIT License — see [LICENSE](LICENSE) for details.

The grammar file (`ModelicaParser/modelica.g4`) is based on the Modelica language specification and is licensed under the BSD license. See the file header for details.

The DymolaInterface is based on Dassault Systèmes' JavaScript interface — see [DymolaInterface/README.md](DymolaInterface/README.md) for license details.
