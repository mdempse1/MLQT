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
`%LocalAppData%/MLQT/mcp-settings.json` (`~/.local/share/MLQT/mcp-settings.json` on Linux), separate
from the desktop app's `settings.json`, which the server only reads, for a simulation tool's install path.

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
directed by each tool's input schema, so a parameter that is genuinely a string is never altered, and a
tool a host built on this server adds is covered as MLQT's are.

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

The instructions and the guidance are hard-wrapped to fit the source, and are **unwrapped before they
are sent** (`Helpers/Prose.cs`): a wrap means nothing to an agent, and sent as written every source
line reached it ending in the checkout's CRLF. A blank line still separates paragraphs and a list item
still starts a line; an item's wrapped lines must be indented past its marker, because a line that is
not ends the list.

**A class id that does not resolve is diagnosed, not just refused** (`ToolDiagnostics.ClassNotFound`,
which every tool taking a class id goes through): nothing loaded; the id's library not loaded, with
how to load it; or the library loaded without that class, naming the deepest package of the id that
does exist. A near match leads when there is one - the same id in another case, or classes elsewhere
with the same last name, which is what an id missing its leading packages finds.

## Building another server on this one

`AddMlqtMcpServer` (`MlqtMcpServerExtensions.cs`) is the whole of MLQT's server except the process: it
registers the services, the tools, the instructions and the coercion filter, and returns the
`IMcpServerBuilder`. MLQT's own `Program.cs` is that call plus a stdio transport, a stderr console
logger and the tool-usage logger. A host serving tools of its own does the same and adds them:

```csharp
builder.Services.AddSingleton<ISettingsService>(new InMemorySettings()); // before: TryAdd keeps it
builder.Services
    .AddMlqtMcpServer(new MlqtMcpServerOptions { ConfigureServer = o => o.ServerInstructions = MyText })
    .WithTools<MyTools>()
    .WithStdioServerTransport();
```

- **It sets the invariant culture**, as `AddMlqtCore` does; a host need not. It is the process-wide
  default, so it reaches every thread of the host that sets no culture of its own - a host that
  formats for its users sets its culture explicitly where it does.
- **Every service is `TryAdd`ed**, so one a host registers first is kept. `HeadlessSettingsService`
  creates and writes `%LocalAppData%/MLQT/mcp-settings.json`; a host that must write nothing there
  registers its own `ISettingsService`.
- **The coercion filter is the first call filter, so the outermost**: a filter the host adds after
  sees the arguments already coerced, and the host's own tools are coerced too, from their schemas.
- **It does not** choose a transport, add a logger, start the NLog file log, or construct a
  `ToolUsageLogger` (which reads its marker file from MLQT's folder) unless the options pass one.
- **Set `<ServerGarbageCollection>true</ServerGarbageCollection>` in the host's project**, as every
  MLQT host does (B281): a check parses from every core, and under the default workstation GC it spends
  most of its time suspended waiting for collections. `HostGarbageCollectionTests` holds MLQT's hosts
  to it, and cannot see a host outside this repository.
- **The instructions are MLQT's** unless `ConfigureServer` replaces them, which it runs after setting
  them. A host whose tools differ should replace rather than append: MLQT's use 1,998 characters of
  a 2,000-character budget, and Claude Code keeps only about the first 2,048, so appended text is
  what gets dropped.
- **The tools are the 16 since B503**, and a host built on an earlier release moves with that one: the
  old names are not kept. `Documentation/mcp-server.md` §Moving from the 66 tools maps each to its
  replacement.

**What a host may rely on** is `AddMlqtMcpServer`, `MlqtMcpServerOptions` and the `ToolUsageLogger`
they name - held by `MlqtMcpServerSurfaceTests` rather than the public-API analyzer, which cannot track
two types without listing every tool class and result type, public only because the SDK binds them -
and the tool names, held by `ToolNamingTests`. A tool's result fields are not part of that promise; a
host that depends on one pins it in its own tests.

## Key concepts

- **Load first.** Almost every tool operates on an in-memory graph. `mlqt_load` takes library
  directories, `.mo` files, encrypted `package.moe` libraries (whose classes come from the vendor's
  shipped documentation), Git/SVN working copies, or a Dymola startup script, and loads their
  dependencies too.
- **Class ids are fully-qualified dotted names** (e.g. `Modelica.Blocks.Continuous.Integrator`). Use
  `mlqt_find` to find one.
- **Analysis runs when needed.** Loading only parses structure. Dependency edges, impact and external
  resources need the dependency analysis (potentially slow), which `mlqt_dependencies`,
  `mlqt_refactor_class` and a library-wide `mlqt_check` run themselves the first time. Query results
  carry a `dependenciesAnalyzed` flag so an empty result is unambiguous.
- **Writes.** `mlqt_write_class`, `mlqt_edit_class`, `mlqt_refactor_class` and `mlqt_format` write
  `.mo` files and update the graph (unless `preview: true`). `mlqt_edit_class`'s `correct_spelling`
  changes the word and nothing else — the file keeps its layout and line endings, so the edit is a
  one-word diff; use `mlqt_format` when reformatting is what you want. `mlqt_format` with `source`
  and `mlqt_check` with `source` are stateless. `mlqt_format` refuses a file holding a class excluded
  from formatting (`__MLQT(format=false)`, `preserveOrder=true`, or the repository's excluded list),
  as the desktop app's formatter does, and its `declarationOrder` option resolves types the way
  `mlqt_check` does, so it clears exactly the `MLQT.Style.DeclarationOrder` findings that tool reports.
- **VCS.** `mlqt_dependencies` maps a working copy's changes, or a revision's, to the classes they
  touched and what depends on them. Generic git/svn (commit, log, push, branch) is left to the CLI.
- Call **`mlqt_get_guidance`** (optionally with a topic) for workflow recipes.

## Tools

| Tool | What it does, and its arguments |
|------|--------------|
| `mlqt_load` | Loads `paths` (library folders, `package.mo` or single `.mo` files, Git/SVN working copies, folders of libraries) and/or the libraries a Dymola startup `script` opens (read, not run; `startDirectory` is the folder Dymola starts in, default the script's own), then their dependencies: `withDependencies` (default true), `tool` (`dymola`, `openmodelica` or `none`, the default) and `toolInstall` say whose library folders to search. `replace` (default false) loads a path over a copy of the same library loaded explicitly; `discoverOnly` (default false) lists a repository's libraries without loading |
| `mlqt_session` | `action`: `status` (the default: each library, its version, class count, `loadedAt`, `origin` (`explicit` or `search`) and the declared dependencies not loaded - all of them with `detailed: true` - and each repository with its branch and revision), `unload` (`library`, by name or id), `reload` from disk (`target`: a `.mo` file, a library or repository by name or id, or nothing for everything), `reset` |
| `mlqt_create_library` | Writes an empty top-level library - `name` (a Modelica identifier) in a new folder under `directory`, holding `package.mo` and an empty `package.order` - with an optional `description` and `version`, and loads it unless `loadIntoSession: false`. Refuses a folder that exists or a name already loaded; `preview` returns the `package.mo` without writing |
| `mlqt_find` | `match`: `name` (the default; with no `query`, a page of every class, filtered by `library` and `classType`), `text` (description and documentation prose), `interface` (shape: `classType`, `hasExperiment`, `minParameters`, `minConnectors`, and `query` as part of the id) or `code` (each line of source containing `query`, with `regex`, `ignoreCase` and `library`). `limit` (default 50) and `offset` page the results |
| `mlqt_browse` | The package tree from `root` (a class id; omit for every library's top), `depth` levels down (default 1, at most 8), cut at `maxNodes` (default 200) |
| `mlqt_get_class` | Up to 20 `classIds`, in the `views` asked for: `info`, `source`, `interface`, `elements`, `behavior`, `connections`, `documentation`, `layout`, `resources` (default `info` and `source`). `includeAnnotations` (source, default false), `includeInherited` (interface and elements, default true), `includeProtected` (elements, default false), `documentationFormat` (`text`, the default, or `html`) |
| `mlqt_get_diagram_image` | Renders `classId`'s diagram as a PNG, `width` pixels wide (default 800, 200-2000) |
| `mlqt_write_class` | Writes `classes`, each `{parentId, source, standalone?}`, in order: created in its parent (and listed in its `package.order`) or, with `replace: true`, replacing the class of that name. `preview` returns each file without writing |
| `mlqt_edit_class` | `operations` on one `classId`, in order and all or none: `replace{old, new, all?}`, `add_component{type, name, modifier?, description?, visibility?, prefix?, condition?, constrainedBy?, comment?}`, `remove_component{name}`, `set_component_modifier{name, modifier}`, `add_equation{equation, comment?}`, `add_connection{portA, portB, comment?}`, `remove_connection{portA, portB}`, `set_component_placement{name, x1, y1, x2, y2, rotation?}`, `set_class_description{description}`, `set_component_description{name, description}`, `set_class_documentation{info?, revisions?}`, `suppress_rule{ruleId, component?, reason?}`, `accept_spelling{word, reason?}`, `correct_spelling{word, newWord}`. `preview` returns the file without keeping it |
| `mlqt_refactor_class` | `action` on `classId`: `rename` (`newName`, a short name), `move` (`newParentId`) or `delete`; a rename or move rewrites every reference. `preview` returns what would change |
| `mlqt_format` | Formats one of `classId` (its file), `library` (every file of it) or `source` (text, returned). `oneOfEachSection`, `importStatementsFirst`, `componentsBeforeClasses` and `declarationOrder` (each default false) choose the rules; `preview` formats without writing |
| `mlqt_check` | `checks` (default `style`): `style`, `spelling` (with suggestions), `references`, `resources`, on one of `classId`, `libraryId` (or nothing, for the library loaded) or `source`. `settings` overrides the style rules for this call only - a rule it leaves out is off; `limit` (default 100) and `offset` page references and resources |
| `mlqt_list_findings` | Pages through the findings known: parse errors (unless `includeParseErrors: false`) and what checks stored, filtered by `severity` (a substring), `source` (`Parser` or `StyleChecking`) and `classId`; `limit` (default 100, at most 1000), `offset` |
| `mlqt_style_settings` | Reads a repository's rules (`repositoryId`, optional with one repository loaded), or merges `settings` in and writes them |
| `mlqt_dependencies` | One target: `classIds` with `direction` (`uses`, the default, or `used_by`; `transitive` with `used_by` is the full impact), `repositoryId` for a working copy's changes (against `revision` if given; `transitive` for their impact), or `resourcePath` for the classes that use a file. `limit` (default 100) and `offset` page transitive answers |
| `mlqt_get_guidance` | Recipes by `topic`: `overview` (the default), `workflows`, `simulators`, `views`, `editing`, `diagrams`, `dependencies`, `style`, `spelling`, `formatting`, `vcs`, `resources` |

`mlqt_check` always reports parse errors (`MLQT.Parse.SyntaxError`, `MLQT.Parse.Failure`) at `Error` severity with source `Parser`, regardless of which style rules are enabled — the same diagnostics the desktop app and `mlqt check` report, with identical wording and line numbers.

A style finding carries the severity the repository configured for its rule, as `Style error`, `Style warning` or `Style info`, so `mlqt_list_findings severity:"error"` selects rules the team set to Error without also matching the parse diagnostics' bare `Error`. Checking a library uses that repository's accepted spellings (`.mlqt/dictionary.txt`) as well as its rules, so the finding count matches the desktop app and `mlqt check` on the same source.

`mlqt_style_settings` **merges**: every rule toggle is optional, and one you leave out keeps the value it had. It writes the repository's committed `.mlqt/settings.json`, so this matters — sending a whole object to change one rule would otherwise rewrite every other rule. Besides the rule toggles it carries `spellCheckLanguages` and `acceptedToolBuiltIns` (`Dymola`, `OpenModelica`, both or neither), each kept when left out. `mlqt_style_settings` reports whether each rule is *switched on*, not whether it would currently run, so reading, changing one key and writing back is faithful even for a rule sitting behind a prerequisite that is off.

## Project layout

- `Program.cs` — the stdio host: transport, stderr logging, the tool-usage logger.
- `MlqtMcpServerExtensions.cs` / `MlqtMcpServerOptions.cs` — `AddMlqtMcpServer`: DI wiring (the desktop host's list, minus what needs a window), tools, instructions, the coercion filter.
- `Tools/` — the 16 tools, in the 11 `[McpServerToolType]` classes, and the classes they call, which hold each behaviour. Those were tools of their own until B503 cut 66 to 16, which is why each still reads as one: its methods are `internal` now, and the tests call them directly.
- `Dtos/` — trimmed, serialization-friendly result types (no UI/layout fields).
- `Helpers/` — editing and resolution helpers: `ClassBodyEditor`, `ModelFilePersistence`, `EntityResolver`, `ModelicaNav`, `GraphRefresh`, `FileWritability`, `ToolDiagnostics`, `EditRollback`, `ReadOnlyClassGuard`, `CodeSearch` (`mlqt_find`'s `code`), the diagram helpers (`DiagramGeometry`, `ConnectionLineAnnotator`, `ConnectorColor`, `ConnectorCompatibility`, `DiagramImage`), and `mlqt_load`'s (`DependencyLoader`, `LibraryCandidates`, `LibraryChoice`, `ModelicaVersion`, `ToolLibraryFolders`, `StartupScriptReader`; the `mcp-library-loading` skill describes them).
- `DiagramImage` composes a class's diagram — each component's own icon at its placement, plus the connection lines — through `ModelicaParser`'s `DiagramSvgRenderer`, and rasterises it with Svg.Skia. **SkiaSharp's drawing is a native library published per runtime identifier**, so `build/publish-tools.sh` checks it is in the tree: it is not loaded until a diagram is asked for, and a tree missing it answers every other tool perfectly.
  The check pipeline itself is **not** here: `StyleCheckRunner`, `StyleCheckContext` and `LibraryCheckSession` live in `MLQT.Services/Checking/`, shared with the CLI and the desktop app so all three report the same findings.
- `Services/HeadlessSettingsService.cs` — JSON settings store, separate from the desktop application's.
- `Services/SessionState.cs` — tracks whether opt-in analysis has run.
- `Services/McpServerJson.cs` / `McpServerJsonContext.cs` — the options every tool is registered with, and the compiled JSON contracts of every record in `Dtos/`, so a client reads the same field names from an obfuscated build (B502). A new result or argument record is listed in the context; `ObfuscationReadinessTests` fails until it is.
- `Services/ToolParameterCheck.cs` — refuses to start a server whose tool parameters have been renamed, which would otherwise bind no client's arguments.
- `Services/ToolArgumentCoercion.cs` — turns a quoted boolean or number into the scalar a tool's input schema asks for.
- `Services/ServerInstructions.cs`, `ToolUsageLogger.cs`, `McpHostSettings.cs` — the connect-time instructions, the opt-in usage log, and the generic host's settings.
- `Helpers/WireNames.cs` — the text each enum in a result is sent as, spelled rather than taken from `ToString()`.
- `dev/smoke.sh` — an `initialize` handshake over stdio. Calling tools by hand is what McpTester is for, and `InProcessServer` in the tests.

## Tests

```bash
dotnet test MLQT.McpServer.Tests
```

`MLQT.McpServer.Tests` exercises the tools against temporary Modelica libraries (real services; VCS
tools use a mocked `IRepositoryService`). Beyond the tools themselves:

- `MlqtMcpServerExtensionsTests` run a real server and client over in-process pipes (`InProcessServer`):
  the tool list, coercion of a host's own tools, filter order, instructions, `TryAdd`.
- `MlqtMcpServerSurfaceTests` hold the surface a host built on this server is promised.
- `ObfuscationReadinessTests` hold what an obfuscated build depends on: every record in the compiled
  contracts with reflection's names, no anonymous object in any shipped assembly, enum text spelled,
  `[Obfuscation]` on every tool class, and `ToolParameterCheck` refusing renamed parameters.
