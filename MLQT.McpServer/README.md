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

**Tool-usage log.** Every tool call is recorded (name, arguments, duration, error) as one JSON object
per line in `%LocalAppData%/MLQT/mcp-tool-usage.jsonl` — handy for reviewing how an agent uses the
server (e.g. views vs. full source). Override the path with the `MLQT_MCP_TOOL_LOG` environment
variable, or set it to `off` to disable.

**Lenient scalar arguments.** Some clients/LLMs send boolean and numeric tool arguments as JSON strings
(e.g. `"standalone":"true"`, `"count":"5"`). A request filter coerces these to the JSON type the
parameter actually declares before the argument is bound, so a quoted scalar behaves the same as the
bare value instead of failing with an opaque "An error occurred invoking '<tool>'". The coercion is
directed by each tool's method signature, so a parameter that is genuinely a string is never altered.

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
| Session / library | `create_library`, `load_repository`, `load_library`, `list_libraries`, `list_repositories`, `discover_libraries`, `reload`, `unload_library` |
| Class query | `get_class_info`, `get_class_source`, `list_classes`, `search_classes`, `get_package_tree` |
| Class views | `get_class_interface`, `list_class_elements`, `get_class_documentation`, `get_class_behavior`, `validate_class_references` |
| Search | `search_text`, `search_by_interface` |
| Documentation | `set_class_description`, `set_component_description`, `set_class_documentation` (read with `get_class_documentation`) |
| Diagram | `get_diagram_layout`, `set_component_placement` |
| Dependencies & impact | `analyze_dependencies`, `get_dependencies`, `find_usages`, `analyze_impact` |
| Code quality | `get_style_settings`, `set_style_settings`, `check_style`, `check_class`, `check_library`, `list_issues` |
| Spelling | `spell_check`, `spelling_suggestions`, `correct_spelling` |
| Editing (class) | `create_class`, `update_class_source`, `rename_class`, `move_class`, `delete_class` |
| Editing (elements) | `add_component`, `remove_component`, `set_component_modifier`, `add_extends`, `add_import`, `add_equation`, `add_statement`, `add_connection`, `remove_connection`, `list_connections`, `batch_edit` |
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
