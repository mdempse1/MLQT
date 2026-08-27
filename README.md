# MLQT — Modelica Library Quality Toolkit

MLQT is an open-source desktop application and set of libraries for managing, analyzing, and reviewing [Modelica](https://modelica.org/) libraries stored in version control systems (Git or SVN).  The project also provides an MCP server focused on creating and editing Modelica models and libraries.

## Origin

MLQT started from a familiar frustration: every time a Modelica tool saved a file, it would introduce a flurry of whitespace and formatting changes that cluttered commits, obscured the real edits in diffs, and made code review painful. The original goal was simple — create a Modelica aware SVN and Git interface.  MLQT puts a layer between Modelica tools and the version control system that applies consistent formatting to every `.mo` file before it was committed, so that Git and SVN diffs showed meaningful changes rather than stylistic churn. Formatting rules are stored in the repository itself so everyone on the team applies the same rules.

From that starting point, MLQT grew into a broader set of tools for working with Modelica code. The same parser that powers the formatter also drives configurable style checking, dependency impact analysis, and external resource tracking, alongside integrations with Dymola and OpenModelica for model checking.

With the dramatic improvements in AI agent capabilities to support Modelica modelling the project has now added an MCP server focused on Modelica model creation and editing.  The goal is to make the LLM more efficient when working with Modelica models by providing more focused information.  For example, using this MCP server, if an LLM wanted to understand the public interface to the Modelica.Blocks.Continuous.Integrator it would have to read the whole Continuous.mo file which is almost 59000 tokens.  Using this MCP server it could call get_class_interface and the returned information is <600 tokens

## What Is MLQT?

Modelica is an object-oriented language for modeling complex physical systems (mechanical, electrical, thermal, hydraulic, etc.). Large Modelica projects typically store their model libraries in Git or SVN and involve teams who need to:

- Review what has changed between revisions
- Understand which other models are affected when a model is modified
- Apply consistent formatting rules to your Modelica code
- Check naming conventions and coding style guidelines
- Check models with simulation tools (Dymola, OpenModelica) before committing
- Track external resources (data files, C libraries, images) referenced by models

MLQT replaces your generic Git or SVN client with a Modelica-aware one. You keep using whichever editor you prefer; MLQT sits between the editor and the repository, filtering out the formatting noise so commits contain only meaningful changes.

The MCP server provides powerful and surgical Modelica editing tools that any AI agent that supports the MCP standard can utilise.  To fully close the loop and empower your AI agent to simulate and verify models, you will also need an MCP server for your Modelica simulation tool of choice.

## Key Features

- **Library Browser** — Browse Modelica package hierarchies with syntax-highlighted code viewing
- **Version Control Integration** — Review uncommitted changes, view history, switch branches, commit, update, merge for both Git and SVN repositories
- **Impact Analysis** — Select a set of models and see the network of models that depend on them, visualized as an interactive graph
- **Style Checking** — Configurable rules enforce coding conventions (description strings, section ordering, naming conventions, Hunspell-based spell checking of descriptions and documentation, etc.)
- **External Resources** — Track all data files, C libraries, and images referenced by models; detect missing files and portability findings
- **Encrypted Libraries** — Commercial libraries ship as an unreadable `package.moe`. MLQT recovers their class names, descriptions, base classes and icons from the vendor's shipped documentation, so references into them resolve and inherited icons are seen instead of being reported as errors; see the [Encrypted Libraries guide](Documentation/encrypted-libraries.md)
- **Code Formatting** — Auto-format Modelica source with configurable rules (section ordering, imports first, annotation placement, etc.)
- **Dymola Integration** — Check and simulate models via Dymola's HTTP JSON-RPC interface
- **OpenModelica Integration** — Check and simulate models via OMC's ZeroMQ interface
- **AI Agent Access (MCP)** — A headless Model Context Protocol server exposes MLQT's Modelica capabilities as tools an AI agent (e.g. Claude) can call to read, author, check and format Modelica code; see the [MCP Server guide](Documentation/mcp-server.md)

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
| [MLQT.McpServer](MLQT.McpServer/) | Headless Model Context Protocol (MCP) server exposing MLQT's Modelica capabilities as tools for AI agents; reuses the service layer without MAUI |
| [MLQT.McpTester](MLQT.McpTester/) | Desktop app for manually testing any stdio MCP server — connect, list tools, auto-generate parameter forms, call, and view results |

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

### Bundling the SVN client

All SVN operations go through the `svn` command-line client (there is no managed
fallback). MLQT can ship its own private copy so end users don't need SVN installed; at
runtime it resolves the executable from the `MLQT_SVN_PATH` environment variable, then the
bundled copy under the app's `svn/` folder, then `svn` on the system `PATH`.

The bundled binaries are **not** stored in source control. For local development this is
fine as long as you have `svn` on your `PATH` (or `MLQT_SVN_PATH` set) — the bundle folder
may be empty. To produce a **distributable** build, populate it first with the SlikSVN
client:

```pwsh
# From a SlikSVN .zip (verify the current URL at https://sliksvn.com/download/)
pwsh build/fetch-svn-tools.ps1 -ZipUrl <SlikSVN-x64-zip-url>
```

See [MLQT/svn-tools/README.md](MLQT/svn-tools/README.md) for the other ways to populate the
folder (local zip, existing install) and how the binaries are copied into the app output.

## Running Tests

```bash
# Run all tests
dotnet test

# Run tests for a specific project
dotnet test ModelicaParser.Tests
dotnet test ModelicaGraph.Tests
dotnet test RevisionControl.Tests
dotnet test MLQT.Services.Tests
dotnet test MLQT.McpServer.Tests
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

Because the business logic is decoupled from the UI, the same service layer also backs a second, headless host — the **MCP server** (`MLQT.McpServer`), which reuses `MLQT.Services`, `ModelicaGraph`, `ModelicaParser` and `RevisionControl` to expose MLQT's capabilities to AI agents over stdio. See the [MCP Server guide](Documentation/mcp-server.md).

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
