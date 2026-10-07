# MLQT.McpServer

A standalone, headless [Model Context Protocol](https://modelcontextprotocol.io) server that exposes
MLQT's Modelica capabilities as tools for AI agents (Claude, etc.). It reuses the MLQT service layer
(`MLQT.Services`, `ModelicaGraph`, `ModelicaParser`, `RevisionControl`) with no UI at all.

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
    "mlqt-modelica": { "command": "C:/Projects/MLQT/MLQT.McpServer/bin/Debug/net10.0/MLQT.McpServer.exe" }
  }
}
```

Logs go to **stderr**; stdout carries the JSON-RPC protocol. Settings persist to
`%LocalAppData%/MLQT/mcp-settings.json`.

**Tool-usage log.** Optionally records every tool call (name, arguments, duration, error) as one JSON
object per line in `%LocalAppData%/MLQT/mcp-tool-usage.jsonl` — handy for reviewing how an agent uses the
server (e.g. views vs. full source). It is **off by default**; enable it by creating an (empty) file named
`mcp-tool-logging.enabled` in `%LocalAppData%/MLQT` (the same folder as the log files), then restart the
server. The `MLQT_MCP_TOOL_LOG` environment variable overrides the marker file: set it to a path to force
logging on at that path, or to `off` to force it off.

**Lenient scalar arguments.** Some clients/LLMs send boolean and numeric tool arguments as JSON strings
(e.g. `"standalone":"true"`, `"count":"5"`). A request filter coerces these to the JSON type the
parameter actually declares before the argument is bound, so a quoted scalar behaves the same as the
bare value instead of failing with an opaque "An error occurred invoking '<tool>'". The coercion is
directed by each tool's method signature, so a parameter that is genuinely a string is never altered.

## What an agent is told

Three layers, each kept to what it is for (and each held by a test in `ToolNamingTests`):

- **The instructions** (`Services/ServerInstructions.cs`), returned on connect. **At most 2,000
  characters**: Claude Code keeps about the first 2,048 and drops the rest, and the 5,000-character
  version before them was cut off before it said anything about editing. They say *when* to use these
  tools rather than an agent's own file tools, how to work beside a simulator's server, and the minimum
  to start. Anything else goes in the guidance.
- **The tool names**, every one `mlqt_`-prefixed, because a simulator's server has a `load_library` and
  a `list_classes` of its own. Each description names Modelica in its first sentence, which is what a
  client that defers tools until searched for matches on.
- **`mlqt_get_guidance`**, the recipes: `overview` carries what the instructions had no room for, and
  `simulators` the division of work with a simulator.

## Key concepts

- **Load first.** Almost every tool operates on an in-memory graph. Use `mlqt_load_repository` (a Git/SVN
  working copy or directory of libraries) or `mlqt_load_library` (one library directory, `.mo` file, or an
  encrypted `package.moe` library, whose classes come from the vendor's shipped documentation).
- **Class ids are fully-qualified dotted names** (e.g. `Modelica.Blocks.Continuous.Integrator`). Use
  `mlqt_search_classes` to find one.
- **Analysis is opt-in.** Loading only parses structure. Dependency edges, impact and external
  resources require `mlqt_analyze_dependencies` first (potentially slow). Style checking is opt-in via
  `mlqt_check_class` / `mlqt_check_library`. Query results carry a `dependenciesAnalyzed` flag so an empty
  result is unambiguous.
- **Writes.** Most tools are read-only. `mlqt_format_class` and `mlqt_correct_spelling` update the graph and
  write the `.mo` file to disk (unless `preview: true`). `mlqt_correct_spelling` changes the word and
  nothing else — the file keeps its layout and line endings, so the edit is a one-word diff; use
  `mlqt_format_class` when reformatting is what you want. `mlqt_format_code` / `mlqt_check_style` are stateless.
  `mlqt_format_class` refuses a file holding a class excluded from formatting (`__MLQT(format=false)`,
  `preserveOrder=true`, or the repository's excluded list), as the desktop app's formatter does, and
  its `declarationOrder` option resolves types the way `mlqt_check_class` does, so it clears exactly the
  `MLQT.Style.DeclarationOrder` findings that tool reports.
- **VCS.** Only two, Modelica-aware, read-only tools are provided. Generic git/svn (commit, log,
  push, branch) is left to the CLI.
- Call **`mlqt_get_guidance`** (optionally with a topic) for workflow recipes.

## Tools

| Group | Tools |
|-------|-------|
| Meta | `mlqt_get_guidance`, `mlqt_server_info` |
| Session / library | `mlqt_create_library`, `mlqt_load_repository`, `mlqt_load_library`, `mlqt_list_libraries`, `mlqt_list_repositories`, `mlqt_discover_libraries`, `mlqt_reload`, `mlqt_unload_library` |
| Class query | `mlqt_get_class_info`, `mlqt_get_class_source`, `mlqt_list_classes`, `mlqt_search_classes`, `mlqt_get_package_tree` |
| Class views | `mlqt_get_class_interface`, `mlqt_list_class_elements`, `mlqt_get_class_documentation`, `mlqt_get_class_behavior`, `mlqt_validate_class_references` |
| Search | `mlqt_search_text`, `mlqt_search_by_interface` |
| Documentation | `mlqt_set_class_description`, `mlqt_set_component_description`, `mlqt_set_class_documentation` (read with `mlqt_get_class_documentation`) |
| Diagram | `mlqt_get_diagram_layout`, `mlqt_get_diagram_image` (renders the diagram as a PNG), `mlqt_set_component_placement` |
| Dependencies & impact | `mlqt_analyze_dependencies`, `mlqt_get_dependencies`, `mlqt_find_usages`, `mlqt_analyze_impact` |
| Code quality | `mlqt_get_style_settings`, `mlqt_set_style_settings`, `mlqt_check_style`, `mlqt_check_class`, `mlqt_check_library`, `mlqt_list_findings`, `mlqt_suppress_rule`, `mlqt_accept_spelling_in_class` |
| Spelling | `mlqt_spell_check`, `mlqt_spelling_suggestions`, `mlqt_correct_spelling` |
| Editing (class) | `mlqt_create_class`, `mlqt_update_class_source`, `mlqt_rename_class`, `mlqt_move_class`, `mlqt_delete_class` |
| Editing (elements) | `mlqt_add_component`, `mlqt_remove_component`, `mlqt_set_component_modifier`, `mlqt_add_extends`, `mlqt_add_import`, `mlqt_add_equation`, `mlqt_add_statement`, `mlqt_add_connection`, `mlqt_remove_connection`, `mlqt_list_connections`, `mlqt_batch_edit` |
| Formatting | `mlqt_format_code`, `mlqt_format_class` |
| External resources | `mlqt_get_class_resources`, `mlqt_find_resource_usages`, `mlqt_get_resource_warnings` |
| Modelica-aware VCS | `mlqt_get_changed_classes`, `mlqt_analyze_change_impact` |

`mlqt_check_class` / `mlqt_check_library` always report parse errors (`MLQT.Parse.SyntaxError`, `MLQT.Parse.Failure`) at `Error` severity with source `Parser`, regardless of which style rules are enabled — the same diagnostics the desktop app and `mlqt check` report, with identical wording and line numbers.

A style finding carries the severity the repository configured for its rule, as `Style error`, `Style warning` or `Style info`, so `mlqt_list_findings severity:"error"` selects rules the team set to Error without also matching the parse diagnostics' bare `Error`. Checking a library uses that repository's accepted spellings (`.mlqt/dictionary.txt`) as well as its rules, so the finding count matches the desktop app and `mlqt check` on the same source.

`mlqt_set_style_settings` **merges**: every rule toggle is optional, and one you leave out keeps the value it had. It writes the repository's committed `.mlqt/settings.json`, so this matters — sending a whole object to change one rule would otherwise rewrite the other twenty-eight. `mlqt_get_style_settings` reports whether each rule is *switched on*, not whether it would currently run, so reading, changing one key and writing back is faithful even for a rule sitting behind a prerequisite that is off.

## Project layout

- `Program.cs` — host, DI wiring (the desktop host's list, minus what needs a window), stdio MCP server.
- `Tools/` — one `[McpServerToolType]` class per group.
- `Dtos/` — trimmed, serialization-friendly result types (no UI/layout fields).
- `Helpers/` — editing and resolution helpers: `ClassBodyEditor`, `ModelFilePersistence`, `EntityResolver`, `ModelicaNav`, `GraphRefresh`, `FileWritability`, `ToolDiagnostics`, and the diagram helpers (`DiagramGeometry`, `ConnectionLineAnnotator`, `ConnectorColor`, `ConnectorCompatibility`, `DiagramImage`).
- `DiagramImage` composes a class's diagram — each component's own icon at its placement, plus the connection lines — through `ModelicaParser`'s `DiagramSvgRenderer`, and rasterises it with Svg.Skia. **SkiaSharp's drawing is a native library published per runtime identifier**, so `build/publish-tools.sh` checks it is in the tree: it is not loaded until a diagram is asked for, and a tree missing it answers every other tool perfectly.
  The check pipeline itself is **not** here: `StyleCheckRunner`, `StyleCheckContext` and `LibraryCheckSession` live in `MLQT.Services/Checking/`, shared with the CLI and the desktop app so all three report the same findings.
- `Services/HeadlessSettingsService.cs` — JSON settings store, separate from the desktop application's.
- `Services/SessionState.cs` — tracks whether opt-in analysis has run.
- `dev/` — stdio test drivers (`smoke.sh`, `mcp_test.py`, `dep_test.py`, `vcs_test.py`).

## Tests

```bash
dotnet test MLQT.McpServer.Tests
```

`MLQT.McpServer.Tests` exercises the tools against temporary Modelica libraries (real services; VCS
tools use a mocked `IRepositoryService`).
