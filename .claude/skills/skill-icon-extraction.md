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
