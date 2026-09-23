using System.Globalization;
using System.Text;
using ModelicaParser.Visitors;

namespace ModelicaParser.Icons;

/// <summary>
/// Renders Modelica Icon data as SVG.
/// Converts IconData with its graphics primitives into an SVG string.
/// </summary>
public static class IconSvgRenderer
{
    /// <summary>
    /// Default size for the rendered SVG in pixels.
    /// </summary>
    public const int DefaultSize = 24;

    /// <summary>
    /// Coordinate units per millimetre used when nobody says otherwise — the value the icon path has
    /// always drawn with, at which Modelica's default 0.25 mm line comes out as 5 units in a 200-unit
    /// icon and so as a visible hairline at 24 px.
    /// </summary>
    public const double DefaultUnitsPerMillimetre = 20;

    /// <summary>
    /// What the surrounding transform does to the primitives, and so what each has to do for itself.
    ///
    /// <para><b>Line thickness and arrow size are physical.</b> Modelica states them in millimetres,
    /// which is a length on the page rather than in the drawing, so the number of coordinate units
    /// they come to depends on how far the caller has zoomed in. An icon at 24 px and a diagram at
    /// 800 px are two very different zooms of the same 200-unit square, and one constant for both
    /// gives a diagram whose every outline is ten pixels thick.</para>
    ///
    /// <para><b>Mirroring is what a reversed extent means</b>, and it applies to the drawing and not
    /// to the writing: Modelica mirrors a component whose Placement extent runs right to left, and
    /// its label still has to be read. <c>MirrorX</c> / <c>MirrorY</c> say that the enclosing
    /// transform flips, so text can undo it.</para>
    /// </summary>
    public sealed record GraphicsContext
    {
        /// <summary>What every caller got before any of this was a question.</summary>
        public static readonly GraphicsContext Default = new();

        /// <summary>Coordinate units to one millimetre, for line thickness and arrow size.</summary>
        public double UnitsPerMillimetre { get; init; } = DefaultUnitsPerMillimetre;

        /// <summary>Whether the enclosing transform mirrors horizontally.</summary>
        public bool MirrorX { get; init; }

        /// <summary>Whether the enclosing transform mirrors vertically.</summary>
        public bool MirrorY { get; init; }
    }

    /// <summary>
    /// Renders IconData as an SVG string.
    /// </summary>
    /// <param name="icon">The IconData to render.</param>
    /// <param name="size">The size in pixels for the output SVG (default 24).</param>
    /// <param name="fileNameResolver">
    /// Optional function to resolve Bitmap fileName references (e.g. modelica:// URIs) to data URIs.
    /// Receives the raw fileName string and should return a data URI or null if unresolvable.
    /// When null, fileName values are used as-is as the image href.
    /// </param>
    /// <returns>An SVG string, or null if icon is null or has no graphics.</returns>
    public static string? RenderToSvg(IconData? icon, int size = DefaultSize, Func<string, string?>? fileNameResolver = null)
    {
        if (icon == null || !icon.HasGraphics)
            return null;

        var sb = new StringBuilder();

        // Calculate coordinate system bounds
        var minX = icon.CoordinateExtent[0];
        var minY = icon.CoordinateExtent[1];
        var maxX = icon.CoordinateExtent[2];
        var maxY = icon.CoordinateExtent[3];
        var width = maxX - minX;
        var height = maxY - minY;

        // SVG header with viewBox for proper scaling
        // Note: Modelica Y-axis is inverted compared to SVG (positive Y is up in Modelica, down in SVG)
        sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{size}\" height=\"{size}\" viewBox=\"{F(minX)} {F(-maxY)} {F(width)} {F(height)}\">");

        // Group with transform to flip Y-axis
        sb.AppendLine($"  <g transform=\"scale(1,-1)\">");

        // Render each graphics primitive
        foreach (var primitive in icon.Graphics)
        {
            if (!primitive.Visible) continue;

            var svg = RenderPrimitive(primitive, fileNameResolver, GraphicsContext.Default);
            if (!string.IsNullOrEmpty(svg))
            {
                sb.AppendLine($"    {svg}");
            }
        }

        sb.AppendLine("  </g>");
        sb.AppendLine("</svg>");

        return sb.ToString();
    }

    /// <summary>
    /// Extracts and renders an icon with inheritance support.
    /// Base class icons are drawn first (as background), derived class icons on top.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code of the class.</param>
    /// <param name="baseClassResolver">Function that resolves a base class name to its Modelica source code.</param>
    /// <param name="size">The size in pixels for the output SVG.</param>
    /// <param name="maxDepth">Maximum inheritance depth to prevent infinite loops (default 10).</param>
    /// <param name="fileNameResolver">
    /// Optional function to resolve Bitmap fileName references to data URIs.
    /// </param>
    /// <param name="initialPackageContext">
    /// Optional package context for the starting class (e.g. "Modelica.Blocks.Discrete" for a class
    /// whose fully-qualified name is "Modelica.Blocks.Discrete.Sampler"). Used to resolve unqualified
    /// extends names when the source code has no 'within' clause (e.g. extracted inner-class snippets).
    /// </param>
    /// <returns>An SVG string, or null if no icon was found.</returns>
    public static string? ExtractAndRenderIconWithInheritance(
        string modelicaCode,
        Func<string, string?> baseClassResolver,
        int size = DefaultSize,
        int maxDepth = 10,
        Func<string, string?>? fileNameResolver = null,
        string? initialPackageContext = null)
    {
        var icon = ExtractIconWithInheritance(modelicaCode, baseClassResolver, maxDepth, initialPackageContext);
        return RenderToSvg(icon, size, fileNameResolver);
    }

    /// <summary>
    /// Extracts and renders an icon with inheritance support from a pre-parsed parse tree.
    /// Base class icons are drawn first (as background), derived class icons on top.
    /// The initial class uses the cached parse tree; base class resolution still uses string-based parsing.
    /// </summary>
    /// <param name="parseTree">The pre-parsed ANTLR4 parse tree for the initial class.</param>
    /// <param name="baseClassResolver">Function that resolves a base class name to its Modelica source code.</param>
    /// <param name="size">The size in pixels for the output SVG.</param>
    /// <param name="maxDepth">Maximum inheritance depth to prevent infinite loops (default 10).</param>
    /// <param name="fileNameResolver">
    /// Optional function to resolve Bitmap fileName references to data URIs.
    /// </param>
    /// <param name="initialPackageContext">
    /// Optional package context for the starting class (e.g. "Modelica.Blocks.Discrete" for a class
    /// whose fully-qualified name is "Modelica.Blocks.Discrete.Sampler"). Used to resolve unqualified
    /// extends names when the source code has no 'within' clause (e.g. extracted inner-class snippets).
    /// </param>
    /// <returns>An SVG string, or null if no icon was found.</returns>
    public static string? ExtractAndRenderIconWithInheritance(
        modelicaParser.Stored_definitionContext parseTree,
        Func<string, string?> baseClassResolver,
        int size = DefaultSize,
        int maxDepth = 10,
        Func<string, string?>? fileNameResolver = null,
        string? initialPackageContext = null)
    {
        var icon = ExtractIconWithInheritance(parseTree, baseClassResolver, maxDepth, initialPackageContext);
        return RenderToSvg(icon, size, fileNameResolver);
    }

    /// <summary>
    /// Extracts an icon with inheritance support, merging base class graphics.
    /// </summary>
    /// <param name="modelicaCode">The Modelica source code of the class.</param>
    /// <param name="baseClassResolver">Function that resolves a base class name to its Modelica source code.</param>
    /// <param name="maxDepth">Maximum inheritance depth to prevent infinite loops.</param>
    /// <param name="initialPackageContext">
    /// Optional package context for the starting class. See ExtractAndRenderIconWithInheritance for details.
    /// </param>
    /// <returns>IconData with merged graphics from all base classes, or null if no icon found.</returns>
    public static IconData? ExtractIconWithInheritance(
        string modelicaCode,
        Func<string, string?> baseClassResolver,
        int maxDepth = 10,
        string? initialPackageContext = null)
    {
        return ExtractIconWithInheritanceInternal(modelicaCode, null, baseClassResolver, maxDepth, new HashSet<string>(), initialPackageContext);
    }

    /// <summary>
    /// Extracts an icon with inheritance support from a pre-parsed parse tree, merging base class graphics.
    /// The initial class uses the cached parse tree; base class resolution still uses string-based parsing.
    /// </summary>
    /// <param name="parseTree">The pre-parsed ANTLR4 parse tree for the initial class.</param>
    /// <param name="baseClassResolver">Function that resolves a base class name to its Modelica source code.</param>
    /// <param name="maxDepth">Maximum inheritance depth to prevent infinite loops.</param>
    /// <param name="initialPackageContext">
    /// Optional package context for the starting class. See ExtractAndRenderIconWithInheritance for details.
    /// </param>
    /// <returns>IconData with merged graphics from all base classes, or null if no icon found.</returns>
    public static IconData? ExtractIconWithInheritance(
        modelicaParser.Stored_definitionContext parseTree,
        Func<string, string?> baseClassResolver,
        int maxDepth = 10,
        string? initialPackageContext = null)
    {
        return ExtractIconWithInheritanceInternal(null, parseTree, baseClassResolver, maxDepth, new HashSet<string>(), initialPackageContext);
    }

    private static IconData? ExtractIconWithInheritanceInternal(
        string? modelicaCode,
        modelicaParser.Stored_definitionContext? parseTree,
        Func<string, string?> baseClassResolver,
        int remainingDepth,
        HashSet<string> visitedClasses,
        string? packageContext)
    {
        if (remainingDepth <= 0)
            return null;

        IconExtractionResult? result;
        if (parseTree != null)
        {
            result = IconExtractor.ExtractIconWithInheritance(parseTree);
        }
        else if (!string.IsNullOrWhiteSpace(modelicaCode))
        {
            result = IconExtractor.ExtractIconWithInheritance(modelicaCode);
        }
        else
        {
            return null;
        }

        if (result == null)
            return null;

        // Effective package for resolving this class's extends:
        // prefer the 'within' clause parsed from this class's own source (works for standalone files),
        // fall back to the package context passed in from the caller (needed for inner-class snippets
        // that have no 'within' clause but whose qualified name was known at the call site).
        var effectivePackage = result.WithinPackage ?? packageContext;

        // Start with this class's icon (may be null if no Icon annotation)
        var mergedIcon = result.Icon;

        // Resolve and collect base class icons in extends-clause order.
        // All icons are gathered first so they can be layered correctly:
        // Modelica draws the 1st extends clause deepest (bottom), each subsequent extends
        // clause on top of the previous, and the class's own graphics on top of everything.
        if (result.HasExtends)
        {
            var baseIcons = new List<IconData>();

            foreach (var baseClassName in result.ExtendsClasses)
            {
                // Prevent infinite loops from circular inheritance
                if (visitedClasses.Contains(baseClassName))
                    continue;

                visitedClasses.Add(baseClassName);

                // Try direct resolution first (handles fully-qualified names and simple cases
                // where the external resolver already knows the package context).
                var baseClassCode = baseClassResolver(baseClassName);
                string? nextPackageContext = null;

                if (!string.IsNullOrEmpty(baseClassCode))
                {
                    // Direct resolution succeeded. If the name is qualified (contains dots) it is
                    // likely the fully-qualified class ID, so derive the owning package from it.
                    // This ensures the next recursion level can resolve its own unqualified extends
                    // (e.g. "Modelica.Blocks.Interfaces.DiscreteSISO" → package "Modelica.Blocks.Interfaces").
                    var dotInBase = baseClassName.LastIndexOf('.');
                    if (dotInBase > 0)
                        nextPackageContext = baseClassName[..dotInBase];
                }
                else if (!string.IsNullOrEmpty(effectivePackage))
                {
                    // Walk up the package hierarchy from effectivePackage to find the base class.
                    // Modelica name resolution rules: try each ancestor package as a prefix until found.
                    // Example: "DiscreteBlock" from package "Modelica.Blocks.Interfaces" tries:
                    //   1. "Modelica.Blocks.Interfaces.DiscreteBlock"  → found (sibling class)
                    var pkg = effectivePackage;
                    while (!string.IsNullOrEmpty(pkg))
                    {
                        var qualifiedName = $"{pkg}.{baseClassName}";
                        if (!visitedClasses.Contains(qualifiedName))
                        {
                            baseClassCode = baseClassResolver(qualifiedName);
                            if (!string.IsNullOrEmpty(baseClassCode))
                            {
                                visitedClasses.Add(qualifiedName);
                                // Derive the package for the next recursion level from the resolved
                                // qualified name: strip the last component to get the owning package.
                                var lastDot = qualifiedName.LastIndexOf('.');
                                nextPackageContext = lastDot > 0 ? qualifiedName[..lastDot] : null;
                                break;
                            }
                        }
                        var dotIdx = pkg.LastIndexOf('.');
                        pkg = dotIdx > 0 ? pkg[..dotIdx] : null;
                    }
                }

                if (string.IsNullOrEmpty(baseClassCode))
                    continue;

                // Recursively get the base class icon (with its own inheritance).
                // Pass nextPackageContext so that inner-class snippets (which have no 'within' clause)
                // can still resolve their own extends using the correct package.
                var baseIcon = ExtractIconWithInheritanceInternal(
                    baseClassCode,
                    null,
                    baseClassResolver,
                    remainingDepth - 1,
                    visitedClasses,
                    nextPackageContext);

                if (baseIcon != null && baseIcon.HasGraphics)
                    baseIcons.Add(baseIcon);
            }

            if (baseIcons.Count > 0)
            {
                // Concatenate all base graphics in extends-clause order: 1st extends is the deepest
                // (bottom) layer, each subsequent extends sits on top of the previous one.
                // This matches how Modelica tools composite multi-extends icons.
                var combinedGraphics = baseIcons.SelectMany(b => b.Graphics).ToList();
                var combinedBase = new IconData
                {
                    CoordinateExtent = baseIcons[0].CoordinateExtent,
                    PreserveAspectRatio = baseIcons[0].PreserveAspectRatio,
                    InitialScale = baseIcons[0].InitialScale,
                    Graphics = combinedGraphics
                };

                // Place combined base layer beneath this class's own graphics
                mergedIcon = mergedIcon == null ? combinedBase : mergedIcon.WithBaseLayer(combinedBase);
            }
        }

        return mergedIcon;
    }

    /// <summary>
    /// The graphics themselves, as an SVG fragment, with no surrounding element. For a caller that
    /// has its own coordinate frame to put them in - a diagram places each component's icon inside a
    /// transform of its own (B196) - and so cannot use a whole document.
    ///
    /// <para>The fragment assumes the Y-flipped group <see cref="RenderToSvg"/> establishes: text
    /// and bitmaps counter-flip themselves, and everything else is written in Modelica coordinates
    /// with Y up.</para>
    /// </summary>
    public static string RenderPrimitives(
        IEnumerable<GraphicsPrimitive> graphics, Func<string, string?>? fileNameResolver = null,
        GraphicsContext? context = null)
    {
        context ??= GraphicsContext.Default;

        var sb = new StringBuilder();
        foreach (var primitive in graphics)
        {
            if (!primitive.Visible)
                continue;
            var svg = RenderPrimitive(primitive, fileNameResolver, context);
            if (!string.IsNullOrEmpty(svg))
                sb.AppendLine(svg);
        }

        return sb.ToString();
    }

    private static string? RenderPrimitive(
        GraphicsPrimitive primitive, Func<string, string?>? fileNameResolver, GraphicsContext context)
    {
        return primitive switch
        {
            RectanglePrimitive rect => RenderRectangle(rect, context),
            EllipsePrimitive ellipse => RenderEllipse(ellipse, context),
            LinePrimitive line => RenderLine(line, context),
            PolygonPrimitive polygon => RenderPolygon(polygon, context),
            TextPrimitive text => RenderText(text, context),
            BitmapPrimitive bitmap => RenderBitmap(bitmap, fileNameResolver),
            _ => null
        };
    }

    private static string RenderRectangle(RectanglePrimitive rect, GraphicsContext context)
    {
        var x1 = rect.Extent[0];
        var y1 = rect.Extent[1];
        var x2 = rect.Extent[2];
        var y2 = rect.Extent[3];

        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);

        var transform = GetTransformAttribute(rect.Origin, rect.Rotation);
        var style = GetStyleAttribute(rect, context);

        if (rect.Radius > 0)
        {
            return $"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" rx=\"{F(rect.Radius)}\" ry=\"{F(rect.Radius)}\"{transform}{style}/>";
        }
        return $"<rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\"{transform}{style}/>";
    }

    private static string RenderEllipse(EllipsePrimitive ellipse, GraphicsContext context)
    {
        var x1 = ellipse.Extent[0];
        var y1 = ellipse.Extent[1];
        var x2 = ellipse.Extent[2];
        var y2 = ellipse.Extent[3];

        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;
        var rx = Math.Abs(x2 - x1) / 2;
        var ry = Math.Abs(y2 - y1) / 2;

        var transform = GetTransformAttribute(ellipse.Origin, ellipse.Rotation);
        var style = GetStyleAttribute(ellipse, context);

        // Full ellipse
        if (ellipse.StartAngle == 0 && ellipse.EndAngle == 360)
        {
            return $"<ellipse cx=\"{F(cx)}\" cy=\"{F(cy)}\" rx=\"{F(rx)}\" ry=\"{F(ry)}\"{transform}{style}/>";
        }

        // Arc (partial ellipse)
        return RenderArc(cx, cy, rx, ry, ellipse.StartAngle, ellipse.EndAngle, ellipse.Closure, transform, style);
    }

    private static string RenderArc(double cx, double cy, double rx, double ry, double startAngle, double endAngle, string closure, string transform, string style)
    {
        // Convert angles to radians
        var startRad = startAngle * Math.PI / 180;
        var endRad = endAngle * Math.PI / 180;

        // Calculate arc endpoints
        var x1 = cx + rx * Math.Cos(startRad);
        var y1 = cy + ry * Math.Sin(startRad);
        var x2 = cx + rx * Math.Cos(endRad);
        var y2 = cy + ry * Math.Sin(endRad);

        // Determine if arc is greater than 180 degrees
        var angleDiff = endAngle - startAngle;
        var largeArc = Math.Abs(angleDiff) > 180 ? 1 : 0;
        var sweep = angleDiff > 0 ? 1 : 0;

        var path = new StringBuilder();
        path.Append($"M {F(x1)} {F(y1)} ");
        path.Append($"A {F(rx)} {F(ry)} 0 {largeArc} {sweep} {F(x2)} {F(y2)}");

        // Add closure
        if (closure == "Radial")
        {
            path.Append($" L {F(cx)} {F(cy)} Z");
        }
        else if (closure == "Chord")
        {
            path.Append(" Z");
        }

        return $"<path d=\"{path}\"{transform}{style}/>";
    }

    private static string RenderLine(LinePrimitive line, GraphicsContext context)
    {
        if (line.Points.Count < 2) return "";

        var transform = GetTransformAttribute(line.Origin, line.Rotation);
        var style = GetLineStyleAttribute(line, context);

        var svg = new StringBuilder();

        if (line.Smooth == "Bezier" && line.Points.Count >= 4)
        {
            svg.Append(RenderBezierLine(line, transform, style));
        }
        else if (line.Points.Count == 2)
        {
            var p1 = line.Points[0];
            var p2 = line.Points[1];
            svg.Append($"<line x1=\"{F(p1[0])}\" y1=\"{F(p1[1])}\" x2=\"{F(p2[0])}\" y2=\"{F(p2[1])}\"{transform}{style}/>");
        }
        else
        {
            var points = string.Join(" ", line.Points.Select(p => $"{F(p[0])},{F(p[1])}"));
            svg.Append($"<polyline points=\"{points}\" fill=\"none\"{transform}{style}/>");
        }

        // A Line's arrow is part of the Line and was being dropped: the label pointing at the PI
        // controller in Modelica.Blocks.Examples.PID_Controller came out as a plain red stroke.
        svg.Append(RenderArrow(line, context, line.ArrowEnd, line.Points[^1], line.Points[^2], transform));
        svg.Append(RenderArrow(line, context, line.ArrowStart, line.Points[0], line.Points[1], transform));

        return svg.ToString();
    }

    /// <summary>
    /// One end's arrowhead, as a triangle on the line's own axis, or nothing when that end has none.
    /// </summary>
    /// <param name="kind">The Modelica <c>Arrow</c> value: None, Open, Filled or Half.</param>
    /// <param name="tip">The point the arrow sits at.</param>
    /// <param name="towards">The neighbouring point, which gives the direction it points away from.</param>
    private static string RenderArrow(
        LinePrimitive line, GraphicsContext context, string? kind,
        double[] tip, double[] towards, string transform)
    {
        if (string.IsNullOrEmpty(kind) || kind == "None")
            return "";

        var dx = tip[0] - towards[0];
        var dy = tip[1] - towards[1];
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length == 0)
            return "";

        // arrowSize is a length on the page, like the line thickness beside it.
        var size = (line.ArrowSize > 0 ? line.ArrowSize : 3) * context.UnitsPerMillimetre;
        var (ux, uy) = (dx / length, dy / length);
        var (px, py) = (-uy, ux);                       // the perpendicular, for the barbs
        var baseX = tip[0] - ux * size;
        var baseY = tip[1] - uy * size;
        var half = size * 0.35;

        var points = string.Join(" ",
            $"{F(tip[0])},{F(tip[1])}",
            $"{F(baseX + px * half)},{F(baseY + py * half)}",
            $"{F(baseX - px * half)},{F(baseY - py * half)}");

        var colour = ColorToHex(line.LineColor);

        // Open draws the barbs and no more; Filled and Half are solid, Half being a single barb that
        // is not worth a shape of its own at the sizes these are drawn at.
        return kind == "Open"
            ? $"<polyline points=\"{points}\" fill=\"none\" stroke=\"{colour}\" "
              + $"stroke-width=\"{W(context.UnitsPerMillimetre * line.LineThickness)}\"{transform}/>"
            : $"<polygon points=\"{points}\" fill=\"{colour}\" stroke=\"{colour}\" "
              + $"stroke-width=\"{W(context.UnitsPerMillimetre * line.LineThickness)}\"{transform}/>";
    }

    private static string RenderBezierLine(LinePrimitive line, string transform, string style)
    {
        var path = new StringBuilder();
        var points = line.Points;

        path.Append($"M {F(points[0][0])} {F(points[0][1])}");

        // Use quadratic bezier curves
        for (int i = 0; i < points.Count - 1; i++)
        {
            double[] midPoint = { 0.5 * (points[i][0] + points[i + 1][0]), 0.5 * (points[i][1] + points[i + 1][1]) };
            path.Append($" Q {F(points[i][0])} {F(points[i][1])}, {F(midPoint[0])} {F(midPoint[1])}");
        }

        return $"<path d=\"{path}\" fill=\"none\"{transform}{style}/>";
    }

    private static string RenderPolygon(PolygonPrimitive polygon, GraphicsContext context)
    {
        if (polygon.Points.Count < 3) return "";

        var transform = GetTransformAttribute(polygon.Origin, polygon.Rotation);
        var style = GetStyleAttribute(polygon, context);

        if (polygon.Smooth == "Bezier" && polygon.Points.Count >= 4)
        {
            return RenderBezierPolygon(polygon, transform, style);
        }

        var points = string.Join(" ", polygon.Points.Select(p => $"{F(p[0])},{F(p[1])}"));
        return $"<polygon points=\"{points}\"{transform}{style}/>";
    }

    private static string RenderBezierPolygon(PolygonPrimitive polygon, string transform, string style)
    {
        var path = new StringBuilder();
        var points = polygon.Points;

        //Move to start point
        double[] startPoint = {0.5 * (points[0][0] + points[points.Count - 1][0]), 0.5 * (points[0][1] + points[points.Count - 1][1])};
        path.Append($"M {F(startPoint[0])} {F(startPoint[1])}");

        // Use quadratic bezier curves
        //For lines with three or more points (P1, P2, . . . , Pn), the middle point of each line segment (P12, P23, . . . ,
        //P(n−1)n) becomes the starting point and ending points of each quadratic Bezier curve. For each quadratic
        //Bezier curve, the common point of the two line segment becomes the control point.
        for (int i = 0; i < points.Count - 1; i++)
        {
            double[] midPoint = { 0.5 * (points[i][0] + points[i + 1][0]), 0.5 * (points[i][1] + points[i + 1][1]) };
            path.Append($" Q {F(points[i][0])} {F(points[i][1])}, {F(midPoint[0])} {F(midPoint[1])}");
        }
        //Add closing segment
        path.Append($" Q {F(points[points.Count - 1][0])} {F(points[points.Count - 1][1])}, {F(startPoint[0])} {F(startPoint[1])}");

        return $"<path d=\"{path}\"{transform}{style}/>";
    }

    private static string RenderText(TextPrimitive text, GraphicsContext context)
    {
        var x1 = text.Extent[0];
        var y1 = text.Extent[1];
        var x2 = text.Extent[2];
        var y2 = text.Extent[3];

        var cx = (x1 + x2) / 2;
        var cy = (y1 + y2) / 2;
        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);

        var transform = GetTransformAttribute(text.Origin, text.Rotation);
        var fontSize = text.FontSize > 0 ? text.FontSize : height * 0.8;

        // Handle text alignment
        var anchor = text.HorizontalAlignment switch
        {
            "Left" => "start",
            "Right" => "end",
            _ => "middle"
        };

        var x = text.HorizontalAlignment switch
        {
            "Left" => x1,
            "Right" => x2,
            _ => cx
        };

        var fillColor = ColorToHex(text.TextColor);
        var fontWeight = text.FontStyles.Contains("Bold") ? " font-weight=\"bold\"" : "";
        var fontStyle = text.FontStyles.Contains("Italic") ? " font-style=\"italic\"" : "";
        var textDecoration = text.FontStyles.Contains("Underline") ? " text-decoration=\"underline\"" : "";
        var fontFamily = !string.IsNullOrEmpty(text.FontName) ? $" font-family=\"{text.FontName}\"" : "";

        // Escape special characters in text
        var displayText = System.Security.SecurityElement.Escape(text.TextString);

        // The parent group has Y flipped, so text counteracts it. Where the caller also mirrors —
        // a component whose Placement extent runs right to left — it counteracts that as well: the
        // drawing is mirrored, the words are not, which is what a Modelica tool shows and what
        // anyone reading a label expects.
        var flipX = context.MirrorX ? -1 : 1;
        var flipY = context.MirrorY ? 1 : -1;

        return $"<text x=\"{F(x * flipX)}\" y=\"{F(-cy * -flipY)}\" font-size=\"{F(fontSize)}\" "
             + $"text-anchor=\"{anchor}\" dominant-baseline=\"middle\" fill=\"{fillColor}\" "
             + $"transform=\"scale({flipX},{flipY})\"{fontFamily}{fontWeight}{fontStyle}{textDecoration}>{displayText}</text>";
    }

    private static string RenderBitmap(BitmapPrimitive bitmap, Func<string, string?>? fileNameResolver)
    {
        var x1 = bitmap.Extent[0];
        var y1 = bitmap.Extent[1];
        var x2 = bitmap.Extent[2];
        var y2 = bitmap.Extent[3];

        var x = Math.Min(x1, x2);
        var y = Math.Min(y1, y2);
        var width = Math.Abs(x2 - x1);
        var height = Math.Abs(y2 - y1);

        // Images need scale(1,-1) to counteract the parent group's Y-axis flip, exactly like text.
        // The combined double-flip leaves image content right-side up while keeping correct position.
        var transform = GetBitmapTransformAttribute(bitmap.Origin, bitmap.Rotation);

        // imageSource takes priority: it is base64-encoded image data embedded directly in the annotation
        if (!string.IsNullOrEmpty(bitmap.ImageSource))
        {
            var mimeType = DetectImageMimeTypeFromBase64(bitmap.ImageSource);
            return $"<image x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" href=\"data:{mimeType};base64,{bitmap.ImageSource}\"{transform}/>";
        }

        // fileName is a file reference — resolve via modelica:// URI or absolute path using the provided resolver
        if (!string.IsNullOrEmpty(bitmap.FileName))
        {
            var href = fileNameResolver?.Invoke(bitmap.FileName) ?? bitmap.FileName;
            if (!string.IsNullOrEmpty(href))
                return $"<image x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" href=\"{href}\"{transform}/>";
        }

        return "";
    }

    /// <summary>
    /// Builds the transform attribute for a Bitmap element, always including scale(1,-1) to
    /// counteract the parent group's Y-axis flip so the image content appears right-side up.
    /// </summary>
    private static string GetBitmapTransformAttribute(double[] origin, double rotation)
    {
        var transforms = new List<string>();
        // scale(1,-1) counters the parent group flip; combined double-flip = identity on content
        transforms.Add("scale(1,-1)");
        if (origin[0] != 0 || origin[1] != 0)
            transforms.Add($"translate({F(origin[0])},{F(origin[1])})");
        if (rotation != 0)
            transforms.Add($"rotate({F(rotation)})");
        return $" transform=\"{string.Join(" ", transforms)}\"";
    }

    /// <summary>
    /// Detects the MIME type of an image from its base64-encoded data by inspecting the magic byte prefix.
    /// </summary>
    private static string DetectImageMimeTypeFromBase64(string base64Data)
    {
        // PNG:  magic bytes \x89PNG  → base64 prefix "iVBOR"
        if (base64Data.StartsWith("iVBOR")) return "image/png";
        // JPEG: magic bytes \xFF\xD8\xFF → base64 prefix "/9j/"
        if (base64Data.StartsWith("/9j/")) return "image/jpeg";
        // BMP:  magic bytes "BM"         → base64 prefix "Qk"
        if (base64Data.StartsWith("Qk")) return "image/bmp";
        // SVG:  text starting with "<sv" → base64 prefix "PHN2"
        //       or XML declaration "<?xm" → base64 prefix "PD94bWw"
        if (base64Data.StartsWith("PHN2") || base64Data.StartsWith("PD94bWw")) return "image/svg+xml";
        return "image/png";
    }

    #region Helper Methods

    private static string GetTransformAttribute(double[] origin, double rotation)
    {
        var transforms = new List<string>();

        if (origin[0] != 0 || origin[1] != 0)
        {
            transforms.Add($"translate({F(origin[0])},{F(origin[1])})");
        }

        if (rotation != 0)
        {
            transforms.Add($"rotate({F(rotation)})");
        }

        if (transforms.Count > 0)
        {
            return $" transform=\"{string.Join(" ", transforms)}\"";
        }
        return "";
    }

    private static string GetStyleAttribute(GraphicsPrimitive primitive, GraphicsContext context)
    {
        var styles = new List<string>();

        // Fill
        if (primitive.FillPattern != "None")
        {
            styles.Add($"fill=\"{ColorToHex(primitive.FillColor)}\"");
        }
        else
        {
            styles.Add("fill=\"none\"");
        }

        // Stroke
        if (primitive.LinePattern != "None")
        {
            styles.Add($"stroke=\"{ColorToHex(primitive.LineColor)}\"");
            styles.Add($"stroke-width=\"{W(context.UnitsPerMillimetre * primitive.LineThickness)}\"");

            // Line pattern
            var dashArray = GetDashArray(primitive.LinePattern);
            if (!string.IsNullOrEmpty(dashArray))
            {
                styles.Add($"stroke-dasharray=\"{dashArray}\"");
            }
        }
        else
        {
            styles.Add("stroke=\"none\"");
        }

        return " " + string.Join(" ", styles);
    }

    private static string GetLineStyleAttribute(LinePrimitive line, GraphicsContext context)
    {
        var styles = new List<string>();

        styles.Add($"stroke=\"{ColorToHex(line.LineColor)}\"");
        styles.Add($"stroke-width=\"{W(context.UnitsPerMillimetre * line.LineThickness)}\"");

        // Line pattern
        var dashArray = GetDashArray(line.LinePattern);
        if (!string.IsNullOrEmpty(dashArray))
        {
            styles.Add($"stroke-dasharray=\"{dashArray}\"");
        }

        return " " + string.Join(" ", styles);
    }

    private static string GetDashArray(string pattern)
    {
        return pattern switch
        {
            "Dash" => "8,4",
            "Dot" => "2,4",
            "DashDot" => "8,4,2,4",
            "DashDotDot" => "8,4,2,4,2,4",
            _ => ""
        };
    }

    private static string ColorToHex(int[] rgb)
    {
        if (rgb.Length < 3) return "#000000";
        return $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}";
    }

    /// <summary>
    /// Formats a double for SVG output (invariant culture, reasonable precision).
    /// </summary>
    private static string F(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A stroke width, which needs more places than a coordinate does. Two decimals is ample for a
    /// position in a 200-unit drawing and not for a line thickness that gets smaller the further in
    /// the view zooms - at 1600 px a hairline is 0.125 units, which "0.##" turns into 0.13, and a
    /// few steps further in it rounds to nothing at all and the line disappears.
    /// </summary>
    private static string W(double value)
    {
        return value.ToString("0.#####", CultureInfo.InvariantCulture);
    }

    #endregion
}
