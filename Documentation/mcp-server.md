# MCP Server (AI Agent Access)

MLQT ships a headless [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that exposes MLQT's Modelica capabilities as tools an AI agent — such as Claude — can call. It lets an assistant read, understand, author, check and format Modelica code in your libraries directly, using the same parser, graph and services that power the desktop application, but without the MAUI UI.

Where the desktop app is for a person working interactively, the MCP server is for an AI agent working on your behalf. The two are complementary: the server operates on the `.mo` files of a loaded library, whether that library lives in a Git/SVN working copy or a plain directory.

The MCP server tells the AI agent:

> The MCP server is about **authoring, checking and formatting Modelica code**. Generic version control (commit, log, push, branch) is intentionally delegated to your own Git/SVN CLI or to the MLQT desktop app — the server provides only the two VCS tools that add Modelica-awareness a plain CLI lacks (mapping a diff to the classes it changed).

## Prerequisites

| Requirement | Details |
|-------------|---------|
| **.NET 10 SDK** | The server is a .NET 10 console application. |
| **An MCP client** | Any MCP-capable client that launches servers over stdio — e.g. Claude Desktop, or the bundled [MLQT.McpTester](#testing-a-server-manually-mcptester). |

The server has no dependency on MAUI, Dymola or OpenModelica; model checking with external tools is intentionally not exposed (use the desktop app for that).  Use a separate MCP server for your chosen Modelica tool to fully close the loop and simulate what this MCP builds.

## Building and registering the server

Build it once so the executable exists:

```bash
dotnet build MLQT.McpServer/MLQT.McpServer.csproj
```

The server speaks MCP over **stdio**. Register it with your client by pointing at the built executable. For Claude Desktop, add it to the `mcpServers` section of the client configuration.  `path_to_mlqt_project_source` is the path to where you have checked out the Git repository on your machine.

```json
{
  "mcpServers": {
    "mlqt": {
      "command": "C:/path_to_mlqt_project_source/MLQT.McpServer/bin/Debug/net10.0/MLQT.McpServer.exe"
    }
  }
}
```

If you are using the Release zip file from Github, then the path to configure the McpServer is different due to the structure of the zip file. `path_to_mlqt_mcp_server_release` is the path to where you have extracted the zip file on your machine.

```json
{
  "mcpServers": {
    "mlqt": {
      "command": "C:/path_to_mlqt_mcp_server_release/MLQT.McpServer.exe"
    }
  }
}
```

After changing the configuration, fully restart the client so it launches the new server process. (A rebuild alone does not affect an already-running server — the client keeps it alive.)

Logs go to **stderr**; **stdout** carries the JSON-RPC protocol, so never write anything else to stdout. Session settings persist to `%LocalAppData%/MLQT/mcp-settings.json`.

## How an agent uses it

The server returns a short set of instructions to the client on connect, and a `get_guidance` tool provides fuller, task-oriented recipes on demand (pass a topic such as `workflows`, `views`, `editing`, `dependencies`, `style`, `spelling`, `formatting`, `vcs`, or `resources`). The essential workflow:

1. **Load first.** Almost every tool operates on an in-memory graph. Load a library with `load_repository` (a Git/SVN working copy or a directory of libraries) or `load_library` (one library directory, its `package.mo`, or a single `.mo` file). To start a brand-new project, `create_library` writes and loads an empty top-level library on disk.

2. **Load the dependencies too.** Loading a library does **not** load the libraries it depends on. Nearly every library builds on the **Modelica Standard Library (MSL)**, and most reference others. The load summary lists a library's declared dependencies (from its `uses` annotation) with the version it expects — load each one so type references resolve. Without dependencies loaded, types cannot be resolved and the agent is reduced to reading raw text; with them loaded, search, the compact "views", reference validation and connector/type checks all work across the whole model. Because the required MSL version varies by project, the agent may ask you for its path.

3. **Learn classes from compact "views" rather than raw source.** `get_class_interface` (public parameters, connectors and, for functions, the signature — with inherited members merged in), `list_class_elements`, `get_class_documentation` and `get_class_behavior` (equations/connections) give an agent what it needs without reading the whole file. `search_classes` also returns each hit's description and a short documentation snippet so the agent can pick the right class — often a higher-level *aggregate* component the library provides — without opening each candidate.

4. **Analysis is opt-in — except parse errors.** Loading only parses structure. Dependency edges, impact analysis and external-resource queries require `analyze_dependencies` to have run first (it can be slow on a large set of libraries). Style checking is opt-in via `check_class` / `check_library`, using each repository's rules. **Parse errors are not opt-in**: `check_class` and `check_library` always report them (`MLQT.Parse.SyntaxError`, `MLQT.Parse.Failure`) at `Error` severity with source `Parser`, even when no style rules are enabled, and `check_class` on a class that failed to parse returns the parse error rather than refusing. Treat one as a stop sign — every other rule reads a parse tree that is missing the code in question, so "no violations" on a file that did not parse means "never looked", not "fine".

5. **Edit surgically.** Element-level tools change one thing without resending the whole class (`add_component`, `set_component_modifier`, `add_connection`, `add_equation`, …), or `create_class` / `update_class_source` / `rename_class` / `move_class` / `delete_class` work at the whole-class level. Every edit is parse-checked with rollback, refuses read-only files, and can be previewed with `preview: true`.

## What the tools cover

The server exposes 60+ tools. The full list is in [MLQT.McpServer/README.md](../MLQT.McpServer/README.md); the groups are:

| Group | Purpose |
|-------|---------|
| **Session / library** | Create, load, list, reload and unload libraries and repositories |
| **Class query** | Look up a class's info and source, list and search classes, browse the package tree |
| **Class views** | Compact interface / elements / documentation / behaviour summaries; reference validation |
| **Search** | Find classes by documentation prose (`search_text`) or by shape (`search_by_interface`) |
| **Editing** | Whole-class and element-level authoring, plus atomic `batch_edit` |
| **Documentation** | Set description strings and the `Documentation(info/revisions)` HTML |
| **Diagram** | Read and set component `Placement`; connection lines are drawn automatically (below) |
| **Dependencies & impact** | Analyse dependencies, find usages, assess the impact of a change |
| **Code quality** | Read/set style settings, run style checks, list issues, and suppress a rule in source (`suppress_rule`) |
| **Spelling** | Spell-check and correct descriptions and documentation |
| **Formatting** | Format a class in place or format a snippet statelessly |
| **External resources** | List resources a class references and report resource warnings |
| **Modelica-aware VCS** | Map a diff to the classes it changed and analyse that change's impact |

## Automatic diagram connections

When you position components on the diagram with `set_component_placement`, the server automatically draws the connection lines: any `connect(...)` whose two components are both placed gets (or has refreshed) a `Line` annotation routed **orthogonally** between the two connector positions, coloured by connector type. It is enough to position the components — no separate call is needed, and moving a component re-routes the lines that touch it.

## Testing a server manually (McpTester)

[MLQT.McpTester](../MLQT.McpTester/README.md) is a small Windows desktop app for exercising **any** stdio MCP server by hand. It launches a server, shows the instructions it returned on connect, lists its tools, generates an input form from each tool's JSON Schema, calls the tool, and shows the result. It is the quickest way to try the MLQT server's tools without wiring up a full AI client.

```bash
dotnet build MLQT.McpTester/MLQT.McpTester.csproj -t:Run
```

The **Use MLQT server** button fills in the built `MLQT.McpServer.exe` path (build `MLQT.McpServer` first). Note that optional booleans render as a three-way selector — `(default)` / `true` / `false` — so an unset tri-state parameter (such as `create_class`'s `standalone`) is omitted rather than sent as `false`.

## Reviewing how an agent worked (tool-usage log)

The server can record every tool call — name, arguments, duration and whether it errored — as one JSON object per line in `%LocalAppData%/MLQT/mcp-tool-usage.jsonl`. This is useful for reviewing how an agent used the server (for example, whether it relied on the compact views or asked for full source).

Tool-usage logging is **off by default**. To turn it on, create a file named `mcp-tool-logging.enabled` in `%LocalAppData%/MLQT` (the same folder the log files are written to) and restart the server — the file can be empty; only its presence matters. To turn logging off again, delete that file. When logging is enabled the server prints the log path to stderr at startup; when it is off it prints a reminder of the file to create.

The `MLQT_MCP_TOOL_LOG` environment variable overrides the marker file: set it to a path to force logging on at that path (regardless of the marker file), or to `off` to force it off.

The server is also tolerant of clients that send boolean or numeric arguments encoded as JSON strings (e.g. `"standalone":"true"`): such scalars are coerced to the type the parameter declares before binding, so a quoted value behaves the same as the bare value.

## Related documentation

- [MLQT.McpServer/README.md](../MLQT.McpServer/README.md) — full tool list, project layout, and developer notes
- [MLQT.McpTester/README.md](../MLQT.McpTester/README.md) — the manual MCP test client
- [Getting Started](getting-started.md) — setting up MLQT and your first repository
