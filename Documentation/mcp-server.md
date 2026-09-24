# MCP Server (AI Agent Access)

MLQT ships a headless [Model Context Protocol](https://modelcontextprotocol.io) (MCP) server that exposes MLQT's Modelica capabilities as tools an AI agent — such as Claude — can call. It lets an assistant read, understand, author, check and format Modelica code in your libraries directly, using the same parser, graph and services that power the desktop application, but with no UI at all.

Where the desktop app is for a person working interactively, the MCP server is for an AI agent working on your behalf. The two are complementary: the server operates on the `.mo` files of a loaded library, whether that library lives in a Git/SVN working copy or a plain directory.

The MCP server tells the AI agent:

> The MCP server is about **authoring, checking and formatting Modelica code**. Generic version control (commit, log, push, branch) is intentionally delegated to your own Git/SVN CLI or to the MLQT desktop app — the server provides only the two VCS tools that add Modelica-awareness a plain CLI lacks (mapping a diff to the classes it changed).

## Prerequisites

| Requirement | Details |
|-------------|---------|
| **MLQT, installed** | The platform installer carries the server; see [Install](#install) below. Nothing else to install — the Windows installer fetches the .NET runtime if it is absent, and the Linux `.deb` bundles it |
| **An MCP client** | Any MCP-capable client that launches servers over stdio — e.g. Claude Desktop, or the bundled [MLQT.McpTester](#testing-a-server-manually-mcptester) |

The server has no dependency on a desktop, Dymola or OpenModelica; model checking with external tools is intentionally not exposed (use the desktop app for that).  Use a separate MCP server for your chosen Modelica tool to fully close the loop and simulate what this MCP builds.

## Install

**With MLQT itself.** There is one installer per platform and it carries the MCP server alongside the desktop application and the `mlqt` CLI — one download, all three. See [installation.md](installation.md).

On **Linux**, the `.deb` puts the server at a fixed path:

```
/usr/bin/mlqt-mcp-server
```

That is a symlink into `/opt/mlqt`, and its stability is the point of it: an agent registers the server by path, and `/opt/mlqt` is on nobody's `PATH`.

On **Windows**, it sits beside the other two tools in the install directory:

| Install mode | Path |
|---|---|
| Just for me *(the default)* | `%LocalAppData%\Programs\MLQT\MLQT.McpServer.exe` |
| For all users | `C:\Program Files\MLQT\MLQT.McpServer.exe` |

Unlike the CLI, the server is **not** added to your `PATH`. An MCP client launches it by full path, so there would be nothing for a `PATH` entry to do.

**From source**, if you are working on MLQT itself: build it as [BUILDING.md](../BUILDING.md) describes, then register the executable it produces under `MLQT.McpServer/bin/`.

## Registering the server with a client

The server speaks MCP over **stdio**. Register it by pointing your client at the executable — for Claude Desktop, in the `mcpServers` section of the client configuration:

```json
{
  "mcpServers": {
    "mlqt": {
      "command": "/usr/bin/mlqt-mcp-server"
    }
  }
}
```

and on Windows, with the path from the table above:

```json
{
  "mcpServers": {
    "mlqt": {
      "command": "C:/Users/<username>/AppData/Local/Programs/MLQT/MLQT.McpServer.exe"
    }
  }
}
```

After changing the configuration, fully restart the client so it launches the new server process. (Reinstalling or rebuilding alone does not affect an already-running server — the client keeps it alive.)

Logs go to **stderr**; **stdout** carries the JSON-RPC protocol, so never write anything else to stdout. Session settings persist to `%LocalAppData%/MLQT/mcp-settings.json`.

## How an agent uses it

The server returns a short set of instructions to the client on connect, and a `get_guidance` tool provides fuller, task-oriented recipes on demand (pass one of `overview`, `workflows`, `views`, `editing`, `diagrams`, `dependencies`, `style`, `spelling`, `formatting`, `vcs` or `resources`). The essential workflow:

1. **Load first.** Almost every tool operates on an in-memory graph. Load a library with `load_repository` (a Git/SVN working copy or a directory of libraries) or `load_library` (one library directory, its `package.mo`, or a single `.mo` file). `load_library` also accepts an **encrypted** library (a directory holding a `package.moe`): its classes are recovered from the vendor's generated documentation so references into it resolve, but it is read-only and never reported on — see [encrypted-libraries.md](encrypted-libraries.md). To start a brand-new project, `create_library` writes and loads an empty top-level library on disk.

2. **Load the dependencies too.** Loading a library does **not** load the libraries it depends on. Nearly every library builds on the **Modelica Standard Library (MSL)**, and most reference others. The load summary lists a library's declared dependencies (from its `uses` annotation) with the version it expects — load each one so type references resolve. Without dependencies loaded, types cannot be resolved and the agent is reduced to reading raw text; with them loaded, search, the compact "views", reference validation and connector/type checks all work across the whole model. Because the required MSL version varies by project, the agent may ask you for its path.

3. **Learn classes from compact "views" rather than raw source.** `get_class_interface` (public parameters, connectors and, for functions, the signature — with inherited members merged in), `list_class_elements`, `get_class_documentation` and `get_class_behavior` (equations/connections) give an agent what it needs without reading the whole file. `search_classes` also returns each hit's description and a short documentation snippet so the agent can pick the right class — often a higher-level *aggregate* component the library provides — without opening each candidate.

   **For a class from an encrypted library the views still answer**, from the vendor's generated
   help rather than from source: `get_class_interface` and `list_class_elements` return its
   parameters, connectors and, for a function, its inputs and outputs, each with the description and
   unit the vendor published. Both results carry `recoveredFromDocumentation: true`, and every
   member's `type` is `null` — the generator does not publish declared types, and MLQT will not
   invent one. `get_class_info` carries the same flag, and such a class is never writable.

4. **Analysis is opt-in — except parse errors.** Loading only parses structure. Dependency edges, impact analysis and external-resource queries require `analyze_dependencies` to have run first (it can be slow on a large set of libraries). Style checking is opt-in via `check_class` / `check_library`, using each repository's rules. **Parse errors are not opt-in**: `check_class` and `check_library` always report them (`MLQT.Parse.SyntaxError`, `MLQT.Parse.Failure`) at `Error` severity with source `Parser`, even when no style rules are enabled, and `check_class` on a class that failed to parse returns the parse error rather than refusing. Treat one as a stop sign — every other rule reads a parse tree that is missing the code in question, so "no findings" on a file that did not parse means "never looked", not "fine". A style finding carries the severity the repository configured for its rule, as `Style error`, `Style warning` or `Style info`, so `list_findings severity:"error"` selects the rules the team set to Error and not the parse diagnostics' bare `Error`.

5. **A finding carries two line numbers, and they are not interchangeable.** `list_findings`
returns `line` — the line in `filePath` — alongside `modelLine`, the same finding's line within the
class's own source. For a class nested a long way down a `package.mo` they are hundreds of lines
apart. Use `line` with `filePath` when editing the file; use `modelLine` when working from
`get_class_source`. The names match the CLI's JSON report so the two surfaces cannot be read as
meaning different things. (`check_class` and `check_library` return the class-relative number only,
under `line`, because they answer a question about a class rather than about a file.)

   `modelLine` indexes `get_class_source` **whether or not the annotations were asked for**. With
   `include_annotations: false` the annotations are cut out of the text rather than the class being
   re-rendered without them: each line comes back as the file wrote it, minus any annotation on it,
   and an annotation written on its own lines leaves them blank rather than closing the gap. So the
   two tools describe the same text, which is what re-rendering could not do.

6. **Edit surgically.** Element-level tools change one thing without resending the whole class (`add_component`, `set_component_modifier`, `add_connection`, `add_equation`, …), or `create_class` / `update_class_source` / `rename_class` / `move_class` / `delete_class` work at the whole-class level. Every edit is parse-checked with rollback, refuses read-only files, and can be previewed with `preview: true`.

## What the tools cover

The server exposes 66 tools. The full list is in [MLQT.McpServer/README.md](../MLQT.McpServer/README.md); the groups are:

| Group | Purpose |
|-------|---------|
| **Session / library** | Create, load, list, reload and unload libraries and repositories |
| **Class query** | Look up a class's info and source, list and search classes, browse the package tree |
| **Class views** | Compact interface / elements / documentation / behaviour summaries; reference validation |
| **Search** | Find classes by documentation prose (`search_text`) or by shape (`search_by_interface`) |
| **Editing** | Whole-class and element-level authoring, plus atomic `batch_edit` |
| **Documentation** | Set description strings and the `Documentation(info/revisions)` HTML |
| **Diagram** | Read and set component `Placement`, render the diagram as an image; connection lines are drawn automatically (below) |
| **Dependencies & impact** | Analyse dependencies, find usages, assess the impact of a change |
| **Code quality** | Read/set style settings, run style checks, list findings, suppress a rule in source (`suppress_rule`), and accept a word's spelling in one class (`accept_spelling_in_class`). `set_style_settings` merges: name only the rules you are changing, and the rest keep their current values — it writes the repository's committed `.mlqt/settings.json`, so an agent that sent a whole object to change one rule would rewrite the lot |
| **Spelling** | Spell-check and correct descriptions and documentation |
| **Formatting** | Format a class in place or format a snippet statelessly |
| **External resources** | List resources a class references and report resource warnings |
| **Modelica-aware VCS** | Map a diff to the classes it changed and analyse that change's impact |

## Automatic diagram connections

When you position components on the diagram with `set_component_placement`, the server automatically draws the connection lines: any `connect(...)` whose two components are both placed gets (or has refreshed) a `Line` annotation routed **orthogonally** between the two connector positions, coloured by connector type. It is enough to position the components — no separate call is needed, and moving a component re-routes the lines that touch it.

## Looking at a diagram

An agent laying out a model places components and wires them up, and is then told in coordinates what
it just did. `get_diagram_image` renders the class instead: each component drawn with its own type's
icon at its `Placement`, the connection lines between them, and whatever the class draws on its own
diagram layer, returned as a PNG. Overlapping components, a signal running right to left and a
connector left on the wrong edge are obvious in the picture and invisible in the numbers.

What it draws is what a Modelica tool draws, which took getting several things right that are easy to
miss and are invisible until a render is put beside one: a Placement's `extent` is stated **relative
to its `origin`**; the components on a diagram include the **inherited** ones (a block usually gets
its `u` and `y` from a base class and declares no connector of its own); a **connector placed on a
diagram is drawn with its diagram layer, not its icon layer**, which are different drawings; a
component shows **its own type's connectors on its icon**, which is what makes a diagram look wired
rather than like a row of boxes; **line thickness and arrow size are millimetres**, so the units they
come to depend on how far the view is zoomed; and a component placed with a reversed extent is
mirrored, but its label is not. `get_diagram_layout` reports the same absolute extents and the same
inherited components, so the numbers and the picture describe one diagram.

A component's parameters are read the way Modelica reads them — the modification the instance was
given, then the type's own default — and that answers two things at once. A connector declared
`if <expr>` **exists only where that expression is true**, so a component whose optional heat port or
support flange was never switched on does not draw one; and an icon labelled `J=%J` says `J=1`.
`get_class_interface` and `list_class_elements` report a conditional connector's `condition` too,
because listing it without saying so tells you that you can connect to a port that may not be there.
Where the expression is past what MLQT evaluates, the connector **is** drawn: showing a port that is
switched off is a smaller lie than hiding one that is switched on.

Two deliberate differences from a Modelica tool's diagram window:

- **Nothing is clipped.** A viewer scales to the declared coordinate system and cuts off anything
  beyond it. Here the view grows to hold everything drawn and the declared canvas is outlined, so a
  component placed off-canvas is the first thing you see rather than the one thing you cannot.
- **A component whose type is not loaded is still drawn**, as a dashed box carrying its name. "This
  library is not loaded" and "there is no component here" must not look the same.

`get_guidance("diagrams")` carries the conventions to lay out by — sizes, the grid, left-to-right
signal flow, and which edge a connector sits on.

## Testing a server manually (McpTester)

[MLQT.McpTester](../MLQT.McpTester/README.md) is a small desktop app, on Windows and Linux, for exercising **any** stdio MCP server by hand. It launches a server, shows the instructions it returned on connect, lists its tools, generates an input form from each tool's JSON Schema, calls the tool, and shows the result. It is the quickest way to try the MLQT server's tools without wiring up a full AI client, and it **displays image content rather than describing it** — so `get_diagram_image` shows you the diagram, which is the only way to judge whether a layout is right.

```bash
dotnet build MLQT.McpTester/MLQT.McpTester.csproj -t:Run
```

The **Use MLQT server** button pre-fills a path to a locally built `MLQT.McpServer.exe`; edit it to wherever your server actually is — the installed paths are under [Install](#install) above. Note that optional booleans render as a three-way selector — `(default)` / `true` / `false` — so an unset tri-state parameter (such as `create_class`'s `standalone`) is omitted rather than sent as `false`.

## Reviewing how an agent worked (tool-usage log)

The server can record every tool call — name, arguments, duration and whether it errored — as one JSON object per line in `%LocalAppData%/MLQT/mcp-tool-usage.jsonl`. This is useful for reviewing how an agent used the server (for example, whether it relied on the compact views or asked for full source).

Tool-usage logging is **off by default**. To turn it on, create a file named `mcp-tool-logging.enabled` in `%LocalAppData%/MLQT` (the same folder the log files are written to) and restart the server — the file can be empty; only its presence matters. To turn logging off again, delete that file. When logging is enabled the server prints the log path to stderr at startup; when it is off it prints a reminder of the file to create.

The `MLQT_MCP_TOOL_LOG` environment variable overrides the marker file: set it to a path to force logging on at that path (regardless of the marker file), or to `off` to force it off.

The server is also tolerant of clients that send boolean or numeric arguments encoded as JSON strings (e.g. `"standalone":"true"`): such scalars are coerced to the type the parameter declares before binding, so a quoted value behaves the same as the bare value.

## When the client says the server disconnected

A client that reports *"Server disconnected"* or *"Server transport closed unexpectedly"* is telling
you the process exited before the protocol got started — which means **MLQT's own log will have
nothing in it**, because the failure happened before logging was set up.

Look in the **client's** log instead, which is where the server's stderr goes. Claude Desktop keeps
one per server at `~/.config/Claude/logs/mcp-server-<name>.log` (`%AppData%\Claude\logs\` on
Windows); Claude Code keeps one per session under `~/.cache/claude-cli-nodejs/<project>/mcp-logs-<name>/`.
The .NET stack trace of whatever went wrong will be in there, above the client's own disconnect
message.

To check the server outside any client, ask it for a handshake directly — this is what the release
build does as a smoke test:

```bash
{ printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"manual","version":"1"}}}'; sleep 5; } | mlqt-mcp-server
```

A working server answers with its name and version. The `sleep` matters: a pipe that closes as soon
as the request is written ends the session before the server has replied, which looks exactly like a
server that cannot start.

On Linux, one startup failure has a cause outside MLQT — see
[inotify limits](troubleshooting.md#file-monitoring-stops-working-on-linux-inotify-limits) in the
troubleshooting guide.

## Related documentation

- [MLQT.McpServer/README.md](../MLQT.McpServer/README.md) — full tool list, project layout, and developer notes
- [MLQT.McpTester/README.md](../MLQT.McpTester/README.md) — the manual MCP test client
- [Getting Started](getting-started.md) — setting up MLQT and your first repository
