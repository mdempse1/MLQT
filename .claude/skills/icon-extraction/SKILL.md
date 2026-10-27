---
name: icon-extraction
description: "Extracting Modelica Icon annotations and rendering them as SVG (IconExtractor, IconSvgRenderer). Use when working on icon parsing or rendering."
---

# Icon Extraction Skill

This skill covers extracting Modelica Icon annotations and rendering them as SVG for display in the UI.

## Overview

Modelica classes can have Icon annotations that define graphical representations. The system extracts these annotations and renders them as inline SVG for the tree view.

## Key Components

| Component | Purpose |
|-----------|---------|
| `IconExtractor` (`ModelicaParser/Visitors`) | Parses Icon annotation from Modelica class definitions |
| `IconExtractionResult` (`ModelicaParser/Visitors`) | An icon plus its `extends` clauses and their `IconMap`/`DiagramMap` |
| `IconSvgRenderer` (`ModelicaParser/Icons`) | Converts extracted IconData to SVG markup; also does the inheritance walk |
| `IconData` (`ModelicaParser/Icons`) | Data model representing parsed Icon graphics |
| `*Primitive` (`ModelicaParser/Icons`) | One class per graphics primitive, over `GraphicsPrimitive` |
| `CoordinateMap`, `GraphicsMapping` (`ModelicaParser/Icons`) | Mapping a base's graphics into an extends region |
| `DiagramSvgRenderer` (`ModelicaParser/Icons`) | The diagram layer's renderer, used by the MCP server's diagram image |

## Supported Graphics Primitives

| Primitive | Properties |
|-----------|------------|
| **Rectangle** | extent, radius (rounded corners), borderPattern, fillColor, lineColor, fillPattern, linePattern |
| **Ellipse** | extent, startAngle, endAngle (for arcs), closure, fillColor, lineColor |
| **Line** | points, color, thickness, arrow (start/end) and arrowSize, smooth (bezier curves) |
| **Polygon** | points, fillColor, lineColor, smooth curves |
| **Text** | extent, textString, fontSize, fontName, textStyle, textColor, horizontalAlignment |
| **Bitmap** | extent, fileName, imageSource (base64 data) |

Every primitive also carries the common `visible`, `origin` and `rotation`.

## Basic Usage

### Extraction and Rendering

```csharp
using ModelicaParser.Icons;      // IconData, IconSvgRenderer
using ModelicaParser.Visitors;   // IconExtractor

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
// Extract icon with inheritance info (one class, no resolution)
IconExtractionResult? result = IconExtractor.ExtractIconWithInheritance(modelicaCode);
// result.Icon - The icon graphics
// result.ExtendsClasses - List of base class names
// result.ExtendsMaps / MapFor(name) / MappedExtends - the IconMap of each clause
// result.WithinPackage - the package unqualified base names are resolved from

// Merge base class graphics
IconData mergedIcon = derivedIconData.WithBaseLayer(baseIconData);

// Full extraction with recursive resolution (IconData, or straight to SVG)
IconData? icon = IconSvgRenderer.ExtractIconWithInheritance(modelicaCode, resolver, maxDepth: 10);
string? svg = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
    modelicaCode,                       // or an already-parsed Stored_definitionContext
    baseClassName => ResolveBaseClassCode(baseClassName), // Resolver function
    size: 24,
    maxDepth: 10,                       // Prevents infinite loops in circular inheritance
    fileNameResolver: f => ResolveBitmap(f),   // optional: modelica:// Bitmap fileName -> path
    initialPackageContext: "Lib.Pkg"    // optional: the enclosing package, when the code has no within
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

Icons are rendered when a class is prepared for the tree, in `LibraryDataService.PrepareModelForDisplay`:

```csharp
var package = ModelicaName.EnclosingPackageOf(model.Id);   // stored code has no within clause
iconSvg = IconSvgRenderer.ExtractAndRenderIconWithInheritance(
    definition.ParsedCode ?? ...,                          // the parse tree when one is held
    baseClassName => ResolveBaseClass(baseClassName, model),
    size: 20,
    fileNameResolver: fileName => ResolveImageFileName(fileName, library),
    initialPackageContext: package.Length > 0 ? package : null);

model.IconSvg = iconSvg;   // ModelNode.IconSvg (stored on its ModelDefinition)
```

A render that throws is **not** remembered as "no icon" (B349); the next refresh tries again. Every
rendered icon is invalidated when libraries change, by bumping `_iconGeneration`, because an icon is
drawn from base classes that may be in the library that just arrived or left.

`LibraryBrowser` renders `IconSvg` when `ModelNode.HasCustomIcon` is true, and otherwise a Material
icon chosen from the class type.

## IconData Structure

Abridged; the files in `ModelicaParser/Icons/` are the reference.

```csharp
public class IconData
{
    public double[] CoordinateExtent { get; set; } = { -100, -100, 100, 100 };  // {x1, y1, x2, y2}
    public bool PreserveAspectRatio { get; set; } = true;
    public double InitialScale { get; set; } = 0.1;
    public bool DeclaresExtent / DeclaresPreserveAspectRatio / DeclaresInitialScale { get; set; }
    public List<GraphicsPrimitive> Graphics { get; set; }
    public bool HasGraphics => Graphics.Count > 0;

    public static IconData ResolveCoordinateSystem(IconData? own, IconData? inherited);
    public IconData WithGraphics(List<GraphicsPrimitive> graphics);
    public IconData WithBaseLayer(IconData? baseIcon);
}

public abstract class GraphicsPrimitive
{
    public abstract string Type { get; }
    public bool Visible { get; set; } = true;
    public double[] Origin { get; set; }
    public double Rotation { get; set; }
    public int[] LineColor { get; set; }
    public int[] FillColor { get; set; }
    public string FillPattern { get; set; } = "None";
    public string LinePattern { get; set; } = "Solid";
    public double LineThickness { get; set; } = 0.25;
}

public class RectanglePrimitive : GraphicsPrimitive { Extent, Radius, BorderPattern }
public class EllipsePrimitive   : GraphicsPrimitive { Extent, StartAngle, EndAngle, Closure }
public class LinePrimitive      : GraphicsPrimitive { Points, ArrowStart, ArrowEnd, ArrowSize, Smooth }
public class PolygonPrimitive   : GraphicsPrimitive { Points, Smooth }
public class TextPrimitive      : GraphicsPrimitive { Extent, TextString, FontSize, FontName, FontStyles, HorizontalAlignment, TextColor }
public class BitmapPrimitive    : GraphicsPrimitive { Extent, FileName /* modelica:// URI */, ImageSource /* base64 */ }
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
// SVG: fill="#ff8000"   (ColorToHex)
```

### Fill Patterns

| Modelica Pattern | SVG Rendering |
|------------------|---------------|
| `FillPattern.None` | No fill (`fill="none"`) |
| Any other pattern (`Solid`, `Horizontal`, `Cross`, gradients, …) | Solid fill in `fillColor` — hatching and gradients are not drawn |

`LinePattern.None` gives `stroke="none"`; the other line patterns become a `stroke-dasharray`.

### Smooth Curves

For `Smooth.Bezier` with at least four points, the renderer converts point lists to SVG bezier curves:

```xml
<path d="M x1,y1 C cx1,cy1 cx2,cy2 x2,y2 ..." />
```

## Key Files

| File | Purpose |
|------|---------|
| `ModelicaParser/Visitors/IconExtractor.cs` | Parses Icon annotation from parse tree |
| `ModelicaParser/Visitors/IconExtractionResult.cs` | Icon plus extends clauses and their maps |
| `ModelicaParser/Icons/IconSvgRenderer.cs` | Renders IconData to SVG string; the inheritance walk |
| `ModelicaParser/Icons/IconData.cs` | Data model for icon graphics, `ResolveCoordinateSystem` |
| `ModelicaParser/Icons/CoordinateMap.cs`, `GraphicsMapping.cs` | Extends-region mapping (B420, B436) |
| `MLQT.Services/LibraryDataService.cs` | Integration with tree building (`PrepareModelForDisplay`) |
| `MLQT.Shared/Components/LibraryBrowser.razor` (+ `.razor.cs`) | Tree view with icon display |
| `MLQT.McpServer/Helpers/DiagramImage.cs` | The MCP server's icon and diagram images |

## Default Icons

When a class has no custom icon, `LibraryBrowser.IconForClassType` picks a Material icon from its class type:

| Class Type | Default Icon |
|------------|--------------|
| function | `Functions` |
| block | `ViewModule` |
| connector | `Power` |
| record | `DataObject` |
| package | `FolderOpen` |
| anything else (model, class, type, …) | `ModelTraining` |

## Performance Considerations

- Icons are rendered once per class when it is prepared for the tree, not on every render
- SVG strings are stored on the class (`ModelNode.IconSvg`), with the icon generation they were drawn in
- Max depth prevents runaway recursion
