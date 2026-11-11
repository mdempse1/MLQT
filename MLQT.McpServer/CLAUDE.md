# MLQT.McpServer

Loaded when working in `MLQT.McpServer/` or `MLQT.McpServer.Tests/`. The repository-wide rules are in
the root `CLAUDE.md`; user documentation is `Documentation/mcp-server.md`, and the API is
`MLQT.McpServer/README.md`. How `mlqt_load` finds dependencies, and what was measured about getting
agents to use the tools, is the `mcp-library-loading` skill.

Headless Model Context Protocol (MCP) server exposing MLQT's Modelica capabilities as tools over
stdio; reuses the service layer with no UI at all.

- **Every tool is `mlqt_`-prefixed**, so it cannot be confused with a simulator's server beside it
- **The connect-time instructions (`Services/ServerInstructions.cs`) stay under 2,000 characters**,
  because Claude Code drops what follows; detail goes in `mlqt_get_guidance`. `ToolNamingTests`
  holds both
- **Another server can be built on it** with `AddMlqtMcpServer` (`MlqtMcpServerExtensions.cs`), which
  is the whole server bar the process - `Program.cs` is it plus stdio; every service is `TryAdd`ed,
  so a host's own registration wins, and the coercion filter reads each tool's schema, so a host's
  tools are coerced too
- **Tool calls run concurrently unless the gate stops them** (`Services/ToolCallGate.cs`). Claude Code
  sends one message's calls together and the SDK runs them as they arrive, while `ILibraryDataService`
  is safe to read only while nothing loads, reloads or edits. A diagram drawn beside a reload showed
  the components as dashed "not loaded" boxes. So the reading calls share and every other call runs
  alone, in arrival order. **A new tool runs alone until it is put on `ToolCallGate.IsShared`'s list**,
  and only a tool that changes nothing in the session may go there - not even recorded findings or the
  dependency flag, which is why `mlqt_check` is not on it. The tools' `ReadOnly` annotations do not
  answer this: they are about the user's files, and `mlqt_load` is `ReadOnly`
- **Every write formats through `EditFormatting.Apply`** (`Helpers/`), with the repository's rules and
  only where its settings have `ApplyFormattingRules` on - never a tool's own call to the renderer, or
  one path formats and its sibling does not. A tool that applies several operations wraps them in
  `EditFormatting.Defer()`, so the file is formatted once at the end and a later operation can name
  text an earlier one wrote as it was written. The settings are the repository's as last read: at
  load, and again by `mlqt_session`'s reload
- **The promised surface** is that method, its options (and the `ToolUsageLogger` they name) and the
  tool names (`MlqtMcpServerSurfaceTests`, `ToolNamingTests`), not the assembly's other public types

**Testing it by hand:** `MLQT.McpTester` is a Photino Blazor desktop app (Windows and Linux) for
manually testing any stdio MCP server: connect, list tools, auto-generate parameter fields from each
tool's JSON Schema, call, and view results - **images included**, shown as pictures rather than as
`[image content]`, which is what makes `mlqt_get_diagram_image` worth having here. Uses MudBlazor +
the ModelContextProtocol client SDK. See `MLQT.McpTester/README.md`.

**Measuring how well an agent uses it:** `TestFixtures/McpEval/` is the library and task list
`build/mcp-eval.py` runs Claude Code against (pass rate, tool calls, errors, calls that bypass MLQT,
tokens), so a change to the toolset can be compared run against run. **It calls the model and costs
money**, so nothing runs it automatically; `python build/mcp-eval.py check-tasks` is free and proves
no task passes on the untouched fixture, and `analyse` reports the same figures for real sessions
from their saved transcripts, also without calling a model. The fixture's flaws are deliberate - see
its README.
