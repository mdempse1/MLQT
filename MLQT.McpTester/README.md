# MLQT.McpTester

A small MAUI Blazor (Windows) desktop app for manually testing **any** stdio MCP server — not just
MLQT's. It launches a server, lists its tools, generates input fields from each tool's JSON Schema,
calls the tool, and shows the result.

Built as a MAUI Blazor Hybrid app (MudBlazor UI) to stay consistent with MLQT itself, and uses the
official `ModelContextProtocol` client SDK.

## Run

```bash
dotnet build MLQT.McpTester/MLQT.McpTester.csproj -t:Run
```

(or open the solution and set `MLQT.McpTester` as the startup project.)

## Use

1. **Command** — path to the MCP server executable. The **Use MLQT server** button fills in the
   built `MLQT.McpServer.exe` path; build `MLQT.McpServer` first so it exists.
2. Optionally set space-separated **Arguments** and a **Working directory**.
3. **Connect** — launches the server over stdio, shows the server's name/version and any **instructions**
   it returned on connect (the text a client/LLM sees describing what the server does), and lists its tools.
4. Pick a tool. A form is generated from its input schema:
   - booleans → switch, enums → dropdown, arrays/objects → multiline (enter JSON), everything else → text.
   - required fields are marked `*`; leave optional fields blank to use the server's defaults.
5. **Call tool** — the result (text content, pretty-printed if JSON, plus any `structuredContent`)
   is shown, with an `ok` / `isError` badge.

## Layout

- `Components/Pages/Home.razor` — the whole tester UI.
- `Services/McpClientService.cs` — holds the live `McpClient` connection (connect / list / call).
- `Services/ToolSchema.cs` — parses a tool's JSON Schema into editable fields and converts them back
  to a typed argument dictionary.
