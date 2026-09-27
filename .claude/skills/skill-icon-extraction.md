# Icon Extraction Skill

This skill covers extracting Modelica Icon annotations and rendering them as SVG for display in the UI.

## Overview

Modelica classes can have Icon annotations that define graphical representations. The system extracts these annotations and renders them as inline SVG for the tree view.

## Key Components

| Component | Purpose |
|-----------|---------|
| `IconExtractor` | Parses Icon annotation from Modelica class definitions |
| `IconSvgRenderer` | Converts extracted IconData to SVG markup |
| `IconData` | Data model representing parsed Icon graphics |

## Supported Graphics Primitives

| Primitive | Properties |
|-----------|------------|
| **Rectangle** | extent, radius (rounded corners), fillColor, lineColor, fillPattern, linePattern |
| **Ellipse** | extent, startAngle, endAngle (for arcs), fillColor, lineColor |
| **Line** | points, color, thickness, arrow styles, smooth (bezier curves) |
| **Polygon** | points, fillColor, lineColor, smooth curves |
| **Text** | extent, textString, fontSize, fontName, textColor, horizontalAlignment |
| **Bitmap** | extent, fileName, imageSource (base64 data) |

## Basic Usage

### Extraction and Rendering

```csharp
using ModelicaParser;

string modelicaCode = @"
model MyModel
  annotation(Icon(graphics={
    Rectangle(extent={{-100,-100},{100,100}}, fillColor={255,255,255}, fillPattern=FillPattern.Solid),
    Ellipse(extent={{-50,-50},{50,50}}, lineColor={0,0,255})
  }));
end MyModel;";
```

### Two-Step Process

```csharp
// Step 1: Extract icon data
IconData? iconData = IconExtractor.ExtractIcon(modelicaCode);

// Step 2: Check if there are graphics and render
if (iconData?.HasGraphics == true)
{
    string? svg = IconSvgRenderer.RenderToSvg(iconData, size: 32);
}
```

## Icon Inheritance

Modelica icons support inheritance through `extends` clauses. When a class extends a base class, the base class icon forms a background layer.

### How It Works

1. Base class icons are drawn first (background)
2. Derived class icons are drawn on top (foreground)
3. Multiple inheritance levels are supported
4. Circular inheritance is prevented with max depth parameter

### Which coordinate system an inherited icon or diagram is drawn in

The Modelica specification's rule (MLS 3.6 §18.6.1.1), implemented once in
`IconData.ResolveCoordinateSystem` and used by both the icon merge and the MCP server's diagram
(`DiagramImage.DiagramSystem`): "The coordinate system attributes (extent and preserveAspectRatio)
of a class are **separately** defined by the following priority: 1. The coordinate system
annotation given in the class (if specified). 2. The coordinate systems of the **first** base class
where the extent on the extends-clause specifies a null-region (if any). 3. The default coordinate
system." (B394)

- **Separately**: `coordinateSystem(preserveAspectRatio=false)` with no extent - common in MSL and
  Buildings - still takes the base's extent. `IconData.Declares*` records which attributes a class
  stated; a stated `{{-100,-100},{100,100}}` and the default look the same otherwise.
- **The first base**, resolved through its own chain - not the first base that states something.
  A first base that states nothing lends the default.
- **A base that draws nothing still lends its system.**
- **Null region**: an extends clause with `IconMap(extent=...)` / `DiagramMap(extent=...)` other than
  `{{0,0},{0,0}}` maps its base into that region (§18.6.3) and does not lend its system.
  `IconExtractionResult.MappedExtends` lists those clauses.

### What an `IconMap` / `DiagramMap` does to the base's graphics (B420)

`IconExtractionResult.ExtendsMaps` holds each clause's map as an `ExtendsMap(Region, PrimitivesVisible)`,
for the layer extracted (`IconMap` for the icon, `DiagramMap` for the diagram); `MapFor(name)` reads one.
Both the icon merge (`IconSvgRenderer.ExtractIconWithInheritance`) and the MCP diagram
(`DiagramImage.InheritedDiagram`, which walks clause by clause for this) apply it:

- **`primitivesVisible=false`** drops the base's graphics, its own bases' included. The base still
  lends its coordinate system, and its components are still drawn - on a diagram they are
  separate, and on an icon the connectors a diagram draws are `DiagramComponent.Children`, not
  graphics. MSL has four such clauses (`Sensors.RelativeAngles`, RobotR3 `AxisType1`, and the
  short-class connectors `ComplexInput`/`ComplexOutput`, whose base a short class definition does not
  follow here anyway).
- **A region** maps the base's resolved coordinate system into it through `GraphicsMapping.Into`: as a
  component's icon goes onto its placement, a reversed region mirroring, and with the scale made
  even and the result centred when the base preserves its aspect ratio. The primitives are rewritten
  rather than wrapped in a transform, so every consumer of the flat list still works; a mirror turns
  a rotation the other way and a quarter turn swaps the axes a scale applies to. Only a non-uniform
  scale of a primitive turned by an angle that is not a multiple of 90 is approximate. Buildings'
  `HeatRecoveryChiller` is the one use in the corpus (`IconMap(extent={{-600,600},{600,-600}})`,
  flipping its base vertically).
- **What else the base contributes goes through the same map (B436).** The arithmetic is
  `CoordinateMap` (`ModelicaParser/Icons`): `Apply` for a primitive, `Placement` for a component's
  placement (extent, rotation and rotation centre - a mirror reverses the extent), `Point` for a
  connect line's points, `Then` to compose two clauses. On the MCP side
  `DiagramGeometry.BaseMaps(libraries, node, layer)` is the one answer to where each base, by id, is
  drawn, composed down the chain; `DiagramGeometry.Placements` maps every inherited placement with
  it, so the connectors on an icon (`Layer.Icon`, what `ConnectorsOn` reads) and the components on a
  diagram land where the base's graphics do, and the router - which ends lines on those same
  components through `PortOf` (B314) - follows without knowing about maps. The image maps a base's
  own `Line(points=...)` with the same map. Buildings' `HeatRecoveryChiller` now has `port_*2` on
  the upper half of its icon, where `Validation.HeatRecoveryChiller`'s Dymola-drawn lines end
  (`(±60,40)`, and `port_*1` at `(±60,-32)`). The desktop icon draws no connectors, so this is MCP
  only.

Before B394 the icon merge kept the derived class's system whenever it had an Icon annotation,
stated or not: 52 Buildings icons (e.g. `DHC.ETS.BaseClasses.CollectorDistributor`) were drawn in
-100..100 when their bases state -200..200 or -300..300.

### API

```csharp
// Extract icon with inheritance info
IconExtractionResult result = IconExtractor.ExtractIconWithInheritance(modelicaCode);
// result.IconData - The icon graphics
// result.ExtendsClauses - List of base class names

// Merge base class graphics
IconData mergedIcon = derivedIconData.WithBaseLayer(baseIconData);

// Full extraction with recursive resolution
string? svg = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
    modelicaCode,
    baseClassName => ResolveBaseClassCode(baseClassName), // Resolver function
    size: 24,
    maxDepth: 10  // Prevents infinite loops in circular inheritance
);
```

### Resolver Function

The resolver function maps base class names to their source code:

```csharp
// Example resolver using a model dictionary
Func<string, string?> resolver = baseClassName =>
{
    if (modelDictionary.TryGetValue(baseClassName, out var model))
    {
        return model.Definition.ModelicaCode;
    }
    return null;
};
```

## Integration with Tree View

Icons are extracted during tree building in `LibraryDataService.BuildModelTree()`:

```csharp
// In tree node creation
var iconSvg = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
    modelCode,
    baseClass => ResolveBaseClass(baseClass, graph),
    size: 18
);

var treeNode = new ModelTreeNode
{
    // ... other properties
    IconSvg = iconSvg  // Custom SVG icon
};
```

The tree view template conditionally renders:
- Custom SVG icon if `IconSvg` is not null
- Default Material Design icon based on class type otherwise

## IconData Structure

```csharp
public class IconData
{
    public double[] CoordinateSystem { get; set; }  // {x1, y1, x2, y2}
    public List<GraphicPrimitive> Graphics { get; set; }
    public bool HasGraphics => Graphics.Count > 0;

    public IconData WithBaseLayer(IconData baseIcon);
}

public abstract class GraphicPrimitive
{
    public double[] Extent { get; set; }
    public int[] LineColor { get; set; }
    public int[] FillColor { get; set; }
    public string FillPattern { get; set; }
    public string LinePattern { get; set; }
    public double LineThickness { get; set; }
}

public class Rectangle : GraphicPrimitive
{
    public double BorderRadius { get; set; }
}

public class Ellipse : GraphicPrimitive
{
    public double StartAngle { get; set; }
    public double EndAngle { get; set; }
}

public class Line : GraphicPrimitive
{
    public List<double[]> Points { get; set; }
    public string Smooth { get; set; }  // "None", "Bezier"
    public string Arrow { get; set; }   // Start/end arrow types
}

public class Polygon : GraphicPrimitive
{
    public List<double[]> Points { get; set; }
    public string Smooth { get; set; }
}

public class Text : GraphicPrimitive
{
    public string TextString { get; set; }
    public double FontSize { get; set; }
    public string FontName { get; set; }
    public string HorizontalAlignment { get; set; }
}

public class Bitmap : GraphicPrimitive
{
    public string FileName { get; set; }      // modelica:// URI
    public string ImageSource { get; set; }   // Base64 encoded data
}
```

## SVG Rendering

### Coordinate System

Modelica uses a coordinate system where:
- Origin (0,0) is at center
- Y-axis points up (positive)
- Default extent is {{-100,-100},{100,100}}

SVG uses:
- Origin (0,0) at top-left
- Y-axis points down (positive)

The renderer transforms coordinates appropriately.

### Color Handling

Modelica colors are RGB arrays `{r, g, b}` with values 0-255:

```csharp
// Modelica: fillColor={255,128,0}
// SVG: fill="rgb(255,128,0)"
```

### Fill Patterns

| Modelica Pattern | SVG Rendering |
|------------------|---------------|
| `FillPattern.Solid` | Solid fill |
| `FillPattern.None` | No fill (transparent) |
| `FillPattern.Horizontal` | Horizontal lines pattern |
| `FillPattern.Vertical` | Vertical lines pattern |
| `FillPattern.Cross` | Cross-hatch pattern |
| etc. | Pattern definitions in SVG defs |

### Smooth Curves

For `Smooth.Bezier`, the renderer converts point lists to SVG cubic bezier curves:

```xml
<path d="M x1,y1 C cx1,cy1 cx2,cy2 x2,y2 ..." />
```

## Key Files

| File | Purpose |
|------|---------|
| `ModelicaParser/IconExtractor.cs` | Parses Icon annotation from parse tree |
| `ModelicaParser/IconSvgRenderer.cs` | Renders IconData to SVG string |
| `ModelicaParser/IconData.cs` | Data model for icon graphics |
| `MLQT.Services/LibraryDataService.cs` | Integration with tree building |
| `MLQT.Shared/Components/LibraryBrowser.razor` | Tree view with icon display |

## Default Icons

When no Icon annotation exists or extraction fails, default Material Design icons are used based on class type:

| Class Type | Default Icon |
|------------|--------------|
| model | `Settings` |
| block | `Dashboard` |
| connector | `ElectricalServices` |
| function | `Functions` |
| record | `TableChart` |
| type | `DataObject` |
| package | `Folder` |
| class | `Code` |

## Performance Considerations

- Icons are extracted once during tree building, not on every render
- SVG strings are stored in `ModelTreeNode.IconSvg`
- Inheritance resolution is cached in the graph
- Max depth prevents runaway recursion
