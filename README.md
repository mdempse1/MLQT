# MLQT — Modelica Library Quality Toolkit

MLQT is an open-source toolkit for teams who develop and maintain Modelica libraries under version control. It is a Modelica-aware Git and SVN client, a quality check you can run on your build server, and an MCP server that lets an AI agent work inside your libraries — all built on one Modelica parser.

**New here?** Start with the [Overview](Documentation/overview.md) for what MLQT does and whether it fits your team, or go straight to the [Getting Started guide](Documentation/getting-started.md).

[Releases](https://github.com/mdempse1/MLQT/releases) · [Documentation](Documentation/) · [MIT licence](LICENSE)

## What is MLQT?

Modelica is an object-oriented language for modeling complex physical systems (mechanical, electrical, thermal, hydraulic, and so on). Large Modelica projects store their libraries in Git or SVN and involve teams who need to:

- Commit real changes rather than the formatting churn their Modelica tool introduces on save
- Review what changed between revisions, at file or model level
- Understand which other models are affected when a model is modified
- Hold the library to a coding standard — consistently, and without fixing a decade of debt first
- Track external resources (data files, C libraries, images) referenced by models
- Check models with Dymola or OpenModelica before committing
- Give an AI agent a way to read and author Modelica that does not involve pasting whole files into a chat window

MLQT replaces your generic Git or SVN client with a Modelica-aware one. You keep using whichever editor you prefer; MLQT sits between the editor and the repository, filtering out the formatting noise so commits contain only meaningful changes. The same capabilities are available headless — as the `mlqt` CLI for your build server, and as an MCP server for AI agents.

## Key features

**Working with libraries**

- **Library Browser** — Browse Modelica package hierarchies with syntax-highlighted code viewing and per-model version control status
- **Version Control Integration** — Review uncommitted changes, view history, switch branches, commit, update, merge, for both Git and SVN
- **Code Formatting** — Auto-format Modelica source with configurable rules (section ordering, imports first, annotation placement, and more)
- **Impact Analysis** — Select a set of models and see the network of models that depend on them, visualized as an interactive graph

**Checking quality**

- **Style Checking** — Configurable rules covering description strings, section ordering, naming conventions, units, `modelica://` reference validation, and spell checking of descriptions and documentation
- **Static Analysis** — Unused elements, classes and imports; duplicate and shadowing declarations; `uses` annotation hygiene; `package.order` consistency; missing units. All self-contained, so they cannot produce false positives from libraries they cannot see
- **Quality Gate & CI** — The [`mlqt` CLI](Documentation/cli.md) runs the same rules headless. Baseline your existing findings as accepted debt and only report on new ones, so the library can never get worse whatever state it starts in. Reports in console, JSON, JUnit, SARIF, TeamCity, markdown and GitHub review formats, plus a Git pre-commit hook. See the [CI quality gate guide](Documentation/ci-quality-gate.md)
- **Metrics & Coverage** — Coverage per quality dimension with compliant/eligible counts, and a trend that accumulates one point per commit that moves the numbers. See [metrics-dashboard.md](Documentation/metrics-dashboard.md)

**Everything else models depend on**

- **External Resources** — Track all data files, C libraries and images referenced by models; detect missing files and portability findings
- **Encrypted Libraries** — Commercial libraries ship as an unreadable `package.moe`. MLQT recovers their class names, descriptions, base classes and icons from the vendor's shipped documentation, so references into them resolve and inherited icons are seen instead of being reported as errors. See the [Encrypted Libraries guide](Documentation/encrypted-libraries.md)
- **Dymola Integration** — Check and simulate models via Dymola's HTTP JSON-RPC interface
- **OpenModelica Integration** — Check and simulate models via OMC's ZeroMQ interface

**AI agent access**

- **MCP Server** — A headless Model Context Protocol server exposes MLQT's Modelica capabilities as more than 80 tools an AI agent (for example, Claude) can call to read, author, check and format Modelica code. Compact class views mean an agent learns a class's interface in a few hundred tokens instead of reading the file it lives in. See the [MCP Server guide](Documentation/mcp-server.md)

## Requirements

There is **one installer per platform, and it carries all three tools** — the desktop application,
the `mlqt` CLI and the MCP server. See [installation.md](Documentation/installation.md).

| | |
|---|---|
| **Operating system** | Windows 10/11, or Ubuntu 22.04 / Debian 12 or newer. x86-64 on both; the Linux build needs WebKitGTK 4.1 |
| **.NET runtime** | The Windows installer fetches .NET 10 if it is absent; the Linux `.deb` bundles it |
| **Version control** | Git, or SVN. The Windows installer bundles an SVN client; the `.deb` recommends `subversion` |
| **Building from source** | .NET 10 SDK — [download](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Dymola** *(optional)* | 2021 or later, for model checking |
| **OpenModelica** *(optional)* | 1.24.0 or later, for model checking |
| **MCP client** *(optional)* | Any client that launches MCP servers over stdio |

## Documentation

User documentation lives in [Documentation/](Documentation/). The pages most people want first:

- [Overview](Documentation/overview.md) — what MLQT does, and what adopting it involves
- [Installation](Documentation/installation.md) — the Windows and Linux installers, and what they put where
- [Getting Started](Documentation/getting-started.md) — set up your first project and repository
- [CI Quality Gate](Documentation/ci-quality-gate.md) — baseline an existing library and gate on new findings
- [CLI reference](Documentation/cli.md) — every `mlqt` command and flag
- [Settings Reference](Documentation/settings-reference.md) — every setting, and where it is stored
- [MCP Server](Documentation/mcp-server.md) — connecting an AI agent

## Project structure

This repository contains the open-source components of MLQT:

| Project | Description |
|---------|-------------|
| [MLQT.Photino](MLQT.Photino/) | The desktop application host, on Photino — Windows and Linux. Bootstraps the UI, DI, the window and the platform services |
| [MLQT.Cli](MLQT.Cli/) | The headless, cross-platform `mlqt` command — check, baseline, compare, hook — for CI and the command line |
| [MLQT.McpServer](MLQT.McpServer/) | Headless MCP server exposing MLQT's capabilities to AI agents; reuses the service layer with no UI at all |
| [MLQT.Shared](MLQT.Shared/) | All Blazor UI: pages, components, layout, application state |
| [MLQT.Services](MLQT.Services/) | Business logic: library management, repository integration, file monitoring, style checking, impact analysis |
| [ModelicaParser](ModelicaParser/) | ANTLR 4 parser for Modelica — parsing, formatting, icon extraction, style rules, resource extraction |
| [ModelicaGraph](ModelicaGraph/) | Directed graph of file/model/resource relationships and dependencies |
| [RevisionControl](RevisionControl/) | Unified Git and SVN interface with workspace management |
| [DymolaInterface](DymolaInterface/) | .NET client for Dymola's HTTP JSON-RPC API |
| [OpenModelicaInterface](OpenModelicaInterface/) | .NET client for OpenModelica Compiler (OMC) via ZeroMQ |
| [MLQT.McpTester](MLQT.McpTester/) | Desktop app (Windows and Linux) for manually testing any stdio MCP server — connect, list tools, auto-generate parameter forms, call, and view results |

Each project has a README with detailed API documentation.

MLQT is a **Blazor application hosted in a native webview** by [Photino](https://www.tryphotino.io/) — WebView2 on Windows, WebKitGTK on Linux — giving a desktop application with a web-based UI and direct filesystem, Git and SVN access from the same process. All business logic lives in service classes behind interfaces, registered as singletons in dependency injection, and the UI communicates with them via events. Because the logic is decoupled from the UI, the same service layer backs two further headless hosts — the CLI and the MCP server. All three run on Windows and Linux.

```
MLQT.Photino (desktop host)      MLQT.Cli          MLQT.McpServer
└── MLQT.Shared (Blazor UI)          │                   │
    └── MLQT.Services ───────────────┴───────────────────┘
        ├── ModelicaGraph (dependency graph)
        │   └── ModelicaParser (ANTLR Modelica parser)
        ├── RevisionControl (Git/SVN)
        ├── DymolaInterface (Dymola HTTP client)
        └── OpenModelicaInterface (OMC ZeroMQ client)
```

## Using the libraries independently

Everything below MLQT.Services is designed to be usable without the rest of MLQT:

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

## Building and contributing

```bash
git clone https://github.com/mdempse1/MLQT.git
cd MLQT
dotnet build MLQT.slnx
dotnet run --project MLQT.Photino/MLQT.Photino.csproj
```

No .NET workload is needed: the desktop host is a plain `net10.0` application, and the same source builds and runs on both platforms.

For the full picture — running the test suites, what CI does and does not cover, coverage targets, bundling the SVN client for a distributable build, and building the installers — see [BUILDING.md](BUILDING.md) and [RELEASING.md](RELEASING.md).

## Origin

MLQT started from a familiar frustration: every time a Modelica tool saved a file, it introduced a flurry of whitespace and formatting changes that cluttered commits, obscured the real edits in diffs, and made code review painful. The original goal was simple — create a Modelica-aware SVN and Git interface that applied consistent formatting to every `.mo` file before it was committed, with the formatting rules stored in the repository so everyone on the team applied the same ones.

Doing that properly meant writing a real parser rather than matching text, and the parser turned out to be the useful part. The same understanding of the language that drives the formatter also drives style checking, the static analyses, dependency impact analysis and external resource tracking, alongside integrations with Dymola and OpenModelica for model checking.

More recently, the improvement in AI agents' ability to work on Modelica made a third host worth building. The MCP server gives an agent focused information instead of raw files: to understand the public interface of `Modelica.Blocks.Continuous.Integrator` an agent would otherwise read the whole of `Continuous.mo`, close to 59,000 tokens; `get_class_interface` returns the same thing in under 600.

## License

MIT License — see [LICENSE](LICENSE) for details.

The grammar file (`ModelicaParser/modelica.g4`) is based on the Modelica language specification and is licensed under the BSD license; see the file header for details. The DymolaInterface is based on Dassault Systèmes' JavaScript interface — see [DymolaInterface/README.md](DymolaInterface/README.md) for license details.
