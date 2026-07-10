# MLQT.McpServer

A standalone, headless [Model Context Protocol](https://modelcontextprotocol.io) server that exposes
MLQT's Modelica capabilities as tools for AI agents (Claude, etc.). It reuses the MLQT service layer
(`MLQT.Services`, `ModelicaGraph`, `ModelicaParser`, `RevisionControl`) without the MAUI UI.

Dymola / OpenModelica model checking is intentionally **not** exposed (those have their own servers).

## Running

```bash
dotnet run --project MLQT.McpServer/MLQT.McpServer.csproj
```

The server speaks MCP over **stdio**. Register it with an MCP client by pointing at the built
executable, e.g. in a client config:

```json
{
  "mcpServers": {
    "mlqt": { "command": "C:/Projects/MLQT/MLQT.McpServer/bin/Debug/net10.0/MLQT.McpServer.exe" }
  }
}
```

Logs go to **stderr**; stdout carries the JSON-RPC protocol. Settings persist to
`%LocalAppData%/MLQT/mcp-settings.json`.

## Key concepts

- **Load first.** Almost every tool operates on an in-memory graph. Use `load_repository` (a Git/SVN
  working copy or directory of libraries) or `load_library` (one library directory or `.mo` file).
- **Class ids are fully-qualified dotted names** (e.g. `Modelica.Blocks.Continuous.Integrator`). Use
  `search_classes` to find one.
- **Analysis is opt-in.** Loading only parses structure. Dependency edges, impact and external
  resources require `analyze_dependencies` first (potentially slow). Style checking is opt-in via
  `check_class` / `check_library`. Query results carry a `dependenciesAnalyzed` flag so an empty
  result is unambiguous.
- **Writes.** Most tools are read-only. `format_class` and `correct_spelling` update the graph and
  write the `.mo` file to disk (unless `preview: true`). `format_code` / `check_style` are stateless.
- **VCS.** Only two, Modelica-aware, read-only tools are provided. Generic git/svn (commit, log,
  push, branch) is left to the CLI.
- Call **`get_guidance`** (optionally with a topic) for workflow recipes.

## Tools

| Group | Tools |
|-------|-------|
| Meta | `get_guidance`, `server_info` |
| Session / library | `load_repository`, `load_library`, `list_libraries`, `list_repositories`, `discover_libraries`, `unload_library` |
| Class query | `get_class_info`, `get_class_source`, `list_classes`, `search_classes`, `get_package_tree` |
| Dependencies & impact | `analyze_dependencies`, `get_dependencies`, `find_usages`, `analyze_impact` |
| Code quality | `get_style_settings`, `set_style_settings`, `check_style`, `check_class`, `check_library`, `list_issues` |
| Spelling | `spell_check`, `spelling_suggestions`, `correct_spelling` |
| Formatting | `format_code`, `format_class` |
| External resources | `get_class_resources`, `find_resource_usages`, `get_resource_warnings` |
| Modelica-aware VCS | `get_changed_classes`, `analyze_change_impact` |

## Project layout

- `Program.cs` — host, DI wiring (mirrors `MauiProgram` minus MAUI), stdio MCP server.
- `Tools/` — one `[McpServerToolType]` class per group.
- `Dtos/` — trimmed, serialization-friendly result types (no UI/layout fields).
- `Helpers/` — `StyleCheckRunner`, `ModelFilePersistence`.
- `Services/HeadlessSettingsService.cs` — JSON settings store (replaces MAUI `Preferences`).
- `Services/SessionState.cs` — tracks whether opt-in analysis has run.
- `dev/` — stdio test drivers (`smoke.sh`, `mcp_test.py`, `dep_test.py`, `vcs_test.py`).

## Tests

```bash
dotnet test MLQT.McpServer.Tests
```

`MLQT.McpServer.Tests` exercises the tools against temporary Modelica libraries (real services; VCS
tools use a mocked `IRepositoryService`).
