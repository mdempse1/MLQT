# MLQT.McpTester

A small Photino Blazor desktop app for manually testing **any** stdio MCP server — not just MLQT's.
It launches a server, lists its tools, generates input fields from each tool's JSON Schema, calls the
tool, and shows the result.

MudBlazor UI, the official `ModelContextProtocol` client SDK, and a Photino window — the same host
MLQT's own desktop app runs in.

**It looks like MLQT because it uses MLQT's theme**, `MLQT.UI.Theme`: the palette, the small dense
controls, the bundled Roboto and the scrollbar fix, through its one project reference. That reference
brings MudBlazor and nothing of Modelica, so the tester is still independent of MLQT's service layer.
Its `wwwroot/index.html` loads the theme's assets in the order `ThemeAssetManifest` gives, with its own
`app.css` between them, and `HostAssetManifestTests` holds the page to that order. This made it the
theme's second consumer in the repository, after MLQT.Shared. Before the theme it asked for the
platform's UI font, to avoid downloading Roboto (B124), and it now uses the theme's local copy.

**It was a MAUI app until phase 7b-1** (2026-09-08), and was ported first on purpose. It is
self-contained, had no project references and has no users to disappoint, so it is the rehearsal for
MLQT's own port: the same host, the same bootstrap, the same MudBlazor under the same engine. It also
had to move regardless — while it was a MAUI app the MAUI workload had to stay installed in CI for
its sake alone. Porting it runs on Linux as well now, which is useful for a tool whose whole job is
launching MCP servers.

## Run

```bash
dotnet run --project MLQT.McpTester/MLQT.McpTester.csproj
```

(or open the solution and set `MLQT.McpTester` as the startup project.)

A plain build works, and until 2026-09-17 it did not. `dotnet publish` writes a real `wwwroot`;
`dotnet build` writes a manifest pointing at the originals — this project's own `wwwroot`, MudBlazor's
folder in the NuGet cache, `_framework/blazor.webview.js` from its package — and the host has to read
that manifest to find any of it. Serving `wwwroot` through a bare `PhysicalFileProvider` found nothing,
so **a Debug build did not start at all**: the provider throws on a root that is not there, before a
window exists. Publishing was the documented workaround.

That is B133, which was found and fixed in `MLQT.Photino` and not here, even though this app was the
rehearsal its port was done on. Both hosts now fall back to the manifest, through the one
`StaticWebAssetManifest` — compiled into this project from source rather than referenced, so the tester
keeps its independence from MLQT's service layer.

## Use

1. **Command** — path to the MCP server executable. It starts filled with MLQT's server when one is
   found (`Services/McpServerLocator.cs`): beside the tester, as the installers lay it out, or in
   `MLQT.McpServer`'s build output for the same configuration and framework. The **Use MLQT server**
   button puts that path back, and is disabled when neither exists; build `MLQT.McpServer` first.
2. Optionally set space-separated **Arguments** and a **Working directory**.
3. **Connect** — launches the server over stdio, shows the server's name/version and any **instructions**
   it returned on connect (the text a client/LLM sees describing what the server does), and lists its tools.
4. Pick a tool. A form is generated from its input schema:
   - booleans → three-way dropdown ((default) / true / false), enums → dropdown, arrays/objects →
     multiline (enter JSON), everything else → text.
   - a field whose schema declares a `default` starts filled with it, and is sent as shown.
   - required fields are marked `*`; leave optional fields blank (and booleans on **(default)**) to use
     the server's defaults. Leaving a boolean on (default) omits it entirely, which matters for tri-state
     nullable boolean parameters (e.g. `mlqt_find`'s `hasExperiment`) where sending `false` is not the same
     as omitting it.
5. **Call tool** — the result is shown with an `ok` / `isError` badge: text content, pretty-printed
   if it is JSON, plus any `structuredContent`.
   - **Image content is displayed, not described.** A tool that returns an image — MLQT's
     `mlqt_get_diagram_image` renders a class's diagram as a PNG — shows the picture above the text, on a
     transparency checkerboard so the edge of the image is visible against the card, with its media
     type and size beneath. **Actual size** switches between fitting the panel and one image pixel
     per screen pixel. Nothing here is MLQT-specific: any server's image content, and any embedded
     resource whose media type is an image, is shown the same way.

## Layout

- `Components/Pages/Home.razor` — the whole tester UI.
- `Services/McpClientService.cs` — holds the live `McpClient` connection (connect / list / call).
- `Services/ToolResultView.cs` — splits a call result into the part that is read and the part that is
  looked at, turning image content into a `data:` URI the page can put in an `img` tag.
- `Services/ToolSchema.cs` — parses a tool's JSON Schema into editable fields and converts them back
  to a typed argument dictionary.
- `Services/McpServerLocator.cs` — where MLQT's own server is, for the default command (B180).
- `Services/StaticWebAssetsFileProvider.cs` — serves the web assets of a built (not published) tester
  through the shared `StaticWebAssetManifest`.
