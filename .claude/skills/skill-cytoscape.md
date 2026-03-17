# Cytoscape Graph Skill

Load this skill when working on graph visualizations, the `CytoscapeGraph` component, `cytoscapeGraph.js`, or the Dependencies page network diagram.

## Overview

The graph visualization system uses [Cytoscape.js](https://js.cytoscape.org/) (MIT, v3.30+) to render interactive network diagrams. It is structured as two layers:

1. **`CytoscapeGraph.razor`** — a generic, reusable Blazor component that knows nothing about Modelica
2. **`cytoscapeGraph.js`** — a window-global JS wrapper that manages Cytoscape instances

The Dependencies page maps domain types (`NetworkNode`/`NetworkEdge`) to the generic `DiagramNode`/`DiagramEdge` types and passes them to `CytoscapeGraph`.

---

## Generic Data Models

Located in `MLQT.Shared/Models/`:

```csharp
public class DiagramNode
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";       // short text shown inside the node circle
    public string FullName { get; set; } = "";    // stored as data, available on click
    public string Color { get; set; } = "#1976d2";
    public string BorderColor { get; set; } = "#0d47a1";
}

public class DiagramEdge
{
    public string FromId { get; set; } = "";
    public string ToId { get; set; } = "";
}
```

---

## CytoscapeGraph.razor Component

**File:** `MLQT.Shared/Components/CytoscapeGraph.razor`

### Parameters

| Parameter | Type | Default | Purpose |
|-----------|------|---------|---------|
| `Nodes` | `IEnumerable<DiagramNode>` | `[]` | Nodes to render |
| `Edges` | `IEnumerable<DiagramEdge>` | `[]` | Edges to render |
| `Height` | `string` | `"400px"` | CSS height of the graph container |
| `Layout` | `string` | `"dagre-tb"` | Layout algorithm name (see below) |
| `OnNodeClicked` | `EventCallback<string>` | — | Fires with node Id when a node is tapped |
| `OnBackgroundClicked` | `EventCallback` | — | Fires when the graph background is tapped |

### Public Methods (call via `@ref`)

```csharp
await _cytoscapeGraph.HighlightNodeAsync(nodeId);  // highlight node + dim others
await _cytoscapeGraph.ClearHighlightAsync();        // remove all highlights
await _cytoscapeGraph.RelayoutAsync(layoutName);    // re-run layout with new algorithm
```

### Lifecycle

- `OnInitialized` — creates `DotNetObjectReference` for JS callbacks
- `OnParametersSetAsync` — if already initialized, calls `cytoscapeGraph.update` when `Nodes`/`Edges` reference changes (detected via `ReferenceEquals`)
- `OnAfterRenderAsync` — performs first-time `cytoscapeGraph.init` once nodes are available (deferred via `_pendingInit` flag)
- `DisposeAsync` — calls `cytoscapeGraph.destroy`, disposes `DotNetObjectReference`

### JS Invokable Callbacks

```csharp
[JSInvokable] public async Task OnNodeClickedFromJs(string nodeId)
[JSInvokable] public async Task OnBackgroundClickedFromJs()
```

### Usage Example

```razor
<CytoscapeGraph @ref="_cytoscapeGraph"
                Nodes="_graphNodes"
                Edges="_graphEdges"
                Height="400px"
                Layout="_selectedLayout"
                OnNodeClicked="OnNodeClicked"
                OnBackgroundClicked="ClearHighlight" />

@code {
    private CytoscapeGraph? _cytoscapeGraph;
    private List<DiagramNode> _graphNodes = new();
    private List<DiagramEdge> _graphEdges = new();

    private async Task OnNodeClicked(string nodeId)
    {
        await _cytoscapeGraph!.HighlightNodeAsync(nodeId);
    }

    private async Task ClearHighlight()
    {
        await _cytoscapeGraph!.ClearHighlightAsync();
    }
}
```

---

## cytoscapeGraph.js

**File:** `MLQT.Shared/wwwroot/cytoscapeGraph.js`

All functions are on `window.cytoscapeGraph`. Instances are stored in `window._cytoscapeInstances` keyed by `containerId`.

Each instance is `{ cy, layoutName, dotNetRef }`.

### API

```javascript
// Initialise a new graph (destroys any existing instance for containerId)
cytoscapeGraph.init(containerId, elements, dotNetRef, layoutName)

// Replace all elements and re-run layout
cytoscapeGraph.update(containerId, elements)

// Re-run layout with a different algorithm
cytoscapeGraph.relayout(containerId, layoutName)

// Highlight a node and dim everything outside its immediate neighbourhood
cytoscapeGraph.highlight(containerId, nodeId)

// Remove all highlighting
cytoscapeGraph.clearHighlight(containerId)

// Destroy instance and free memory
cytoscapeGraph.destroy(containerId)
```

### Element Format

Elements passed to `init`/`update` are built by `BuildElements()` in `CytoscapeGraph.razor`:

```javascript
// Node element
{ group: "nodes", data: { id, label, color, borderColor, fullName } }

// Edge element
{ group: "edges", data: { id: "edge-{fromId}-{toId}", source: fromId, target: toId } }
```

### Node Visual Styles

| CSS selector | Meaning |
|---|---|
| `node` | Default — colored circle with white label |
| `node.highlighted` | Clicked node — larger (48px), black border |
| `node.dimmed` | Not in neighbourhood of highlighted node — opacity 0.2 |
| `edge.highlighted` | Edge connected to highlighted node — blue, thicker |
| `edge.dimmed` | Edge not in neighbourhood — opacity 0.1 |

---

## Layout Options

The `Layout` parameter / `cytoscapeGraph.relayout` accepts these strings:

| Value | Algorithm | Notes |
|-------|-----------|-------|
| `dagre-tb` | Dagre (top→bottom) | Default; best for dependency trees |
| `dagre-lr` | Dagre (left→right) | Wide graphs |
| `breadthfirst` | BFS tree | Good for shallow trees |
| `klay` | KLay | Hierarchical, good alternative to dagre |
| `concentric` | Concentric rings | Degree-based concentric layout |
| `fcose` | fCoSE force-directed | Best for dense graphs |
| `spread` | Spread | Maximises use of canvas area |
| `cose` | CoSE (built-in) | Basic force-directed, no extra deps |
| `circle` | Circle | All nodes on a circle |

---

## Script Loading Order

**File:** `MLQT/wwwroot/index.html`

Scripts must be loaded in this exact order (each extension auto-registers when `window.cytoscape` is defined):

```html
<script src="_content/MLQT.Shared/lib/cytoscape.min.js"></script>
<script src="_content/MLQT.Shared/lib/dagre.min.js"></script>
<script src="_content/MLQT.Shared/lib/cytoscape-dagre.js"></script>
<script src="_content/MLQT.Shared/lib/klayjs.js"></script>
<script src="_content/MLQT.Shared/lib/cytoscape-klay.js"></script>
<script src="_content/MLQT.Shared/lib/weaver.min.js"></script>       <!-- peer dep for spread -->
<script src="_content/MLQT.Shared/lib/cytoscape-spread.js"></script>
<script src="_content/MLQT.Shared/lib/layout-base.js"></script>       <!-- peer dep for cose-base -->
<script src="_content/MLQT.Shared/lib/cose-base.js"></script>         <!-- peer dep for fcose -->
<script src="_content/MLQT.Shared/lib/cytoscape-fcose.js"></script>
<script src="_content/MLQT.Shared/cytoscapeGraph.js"></script>
```

All files are in `MLQT.Shared/wwwroot/lib/` (offline UMD bundles — no CDN, required for MAUI BlazorWebView).

**Peer dependency chain:**
- `spread` requires `weaver.min.js` → exports `window.weaver`
- `fcose` requires `cose-base.js` which requires `layout-base.js` → exports `window.layoutBase` → `window.coseBase`

Extensions auto-register via `if (typeof cytoscape !== 'undefined') { register(cytoscape); }` at load time. No manual `cytoscape.use()` calls needed.

---

## Dependencies Page Integration

**File:** `MLQT.Shared/Pages/Dependencies.razor`

The page maps impact analysis results to `DiagramNode`/`DiagramEdge`:

```csharp
// Node color convention
// Selected model:  #1976d2 (blue)   border #0d47a1
// Impacted model:  #ed6c02 (orange) border #e65100
// Both selected and impacted: #9c27b0 (purple) border #6a1b9a
// (colors assigned by IImpactAnalysisService.AnalyzeImpact)

_graphNodes = _networkNodes.Select(n => new DiagramNode {
    Id = n.Id, Label = n.ShortName, FullName = n.FullName,
    Color = n.Color, BorderColor = n.BorderColor
}).ToList();

_graphEdges = _networkEdges.Select(e => new DiagramEdge {
    FromId = e.FromId, ToId = e.ToId
}).ToList();
```

Layout is changed directly (not via parameter re-render):
```csharp
private async Task OnLayoutChanged(string layoutName)
{
    _selectedLayout = layoutName;
    if (_cytoscapeGraph != null)
        await _cytoscapeGraph.RelayoutAsync(layoutName);
}
```

---

## Key Files

| File | Purpose |
|------|---------|
| `MLQT.Shared/Components/CytoscapeGraph.razor` | Reusable Blazor graph component |
| `MLQT.Shared/Models/DiagramNode.cs` | Generic graph node data model |
| `MLQT.Shared/Models/DiagramEdge.cs` | Generic graph edge data model |
| `MLQT.Shared/wwwroot/cytoscapeGraph.js` | JS wrapper — all Cytoscape interop |
| `MLQT.Shared/wwwroot/lib/` | Offline JS library bundles |
| `MLQT/wwwroot/index.html` | Script loading order |
| `MLQT.Shared/Pages/Dependencies.razor` | Consumer of `CytoscapeGraph` |
| `MLQT.Services/Interfaces/IImpactAnalysisService.cs` | Service providing nodes/edges |

---

## Adding a New Graph View

To use `CytoscapeGraph` in a new page:

1. Add `private CytoscapeGraph? _cytoscapeGraph;` and lists of `DiagramNode`/`DiagramEdge`
2. Map your domain data to `DiagramNode` (set `Id`, `Label`, `Color`, `BorderColor`, `FullName`)
3. Add `<CytoscapeGraph @ref="..." Nodes="..." Edges="..." OnNodeClicked="..." OnBackgroundClicked="..." />`
4. Handle `OnNodeClicked(string nodeId)` — typically calls `HighlightNodeAsync`
5. Handle `OnBackgroundClicked` — typically calls `ClearHighlightAsync`

No changes to `index.html` or `cytoscapeGraph.js` are needed for new consumers.
