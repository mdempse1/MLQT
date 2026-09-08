# MLQT.McpTester

A small Photino Blazor desktop app for manually testing **any** stdio MCP server — not just MLQT's.
It launches a server, lists its tools, generates input fields from each tool's JSON Schema, calls the
tool, and shows the result.

MudBlazor UI, the official `ModelContextProtocol` client SDK, and a Photino window — consistent with
where MLQT itself is going.

**It was a MAUI app until phase 7b-1** (2026-09-08), and was ported first on purpose. It is
self-contained, has no project references and no users to disappoint, so it is the rehearsal for
MLQT's own port: the same host, the same bootstrap, the same MudBlazor under the same engine. It also
had to move regardless — while it was a MAUI app the MAUI workload had to stay installed in CI for
its sake alone. Porting it runs on Linux as well now, which is useful for a tool whose whole job is
launching MCP servers.

## Run

**`dotnet run` does not work, and fails silently.** Photino serves `wwwroot` through a bare
`PhysicalFileProvider` with no support for the static-web-assets manifest, so a plain build leaves no
`wwwroot/_content` and no `_framework/blazor.webview.js` in the output. The window opens, the host
starts, and nothing renders — no error, no blank-page exception, just an empty window. Publish
instead:

```bash
dotnet publish MLQT.McpTester/MLQT.McpTester.csproj -c Release -o publish
./publish/MLQT.McpTester        # or publish\MLQT.McpTester.exe on Windows
```

This is the same constraint the main application will have once it moves to Photino, and phase 7b-2
owns making it less awkward. Recorded here because the symptom looks like a broken app rather than a
missing build step.

(or open the solution and set `MLQT.McpTester` as the startup project.)

## Use

1. **Command** — path to the MCP server executable. The **Use MLQT server** button fills in the
   built `MLQT.McpServer.exe` path; build `MLQT.McpServer` first so it exists.
2. Optionally set space-separated **Arguments** and a **Working directory**.
3. **Connect** — launches the server over stdio, shows the server's name/version and any **instructions**
   it returned on connect (the text a client/LLM sees describing what the server does), and lists its tools.
4. Pick a tool. A form is generated from its input schema:
   - booleans → three-way dropdown ((default) / true / false), enums → dropdown, arrays/objects →
     multiline (enter JSON), everything else → text.
   - required fields are marked `*`; leave optional fields blank (and booleans on **(default)**) to use
     the server's defaults. Leaving a boolean on (default) omits it entirely, which matters for tri-state
     `boolean|null` parameters (e.g. `create_class`'s `standalone`) where sending `false` is not the same
     as omitting it.
5. **Call tool** — the result (text content, pretty-printed if JSON, plus any `structuredContent`)
   is shown, with an `ok` / `isError` badge.

## Layout

- `Components/Pages/Home.razor` — the whole tester UI.
- `Services/McpClientService.cs` — holds the live `McpClient` connection (connect / list / call).
- `Services/ToolSchema.cs` — parses a tool's JSON Schema into editable fields and converts them back
  to a typed argument dictionary.
