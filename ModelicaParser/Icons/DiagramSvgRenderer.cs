using System.Globalization;
using System.Text;

namespace ModelicaParser.Icons;

/// <summary>One component on a diagram: where it sits, and what it looks like.</summary>
/// <param name="Name">The component's declared name, substituted for <c>%name</c> in its icon text.</param>
/// <param name="Extent">Its Placement extent as [x1,y1,x2,y2] in the parent's diagram coordinates.
/// A reversed pair mirrors the icon, which is what Modelica means by it.</param>
/// <param name="Rotation">Placement rotation in degrees, counter-clockwise, about the extent's centre.</param>
/// <param name="Icon">The component type's icon, merged down its extends chain, or null when the type
/// could not be resolved or draws nothing — in which case an outline is drawn in its place.</param>
/// <param name="TypeName">The declared type, shown in the placeholder when there is no icon.</param>
/// <param name="RotationCentre">
/// The point <paramref name="Rotation"/> turns about, as [x,y]. Null means the extent's own centre,
/// which is what a transformation with no <c>origin</c> amounts to.
/// </param>
/// <param name="Children">
/// What is drawn <em>inside</em> this component's icon, in the icon's own coordinates: its
/// connectors. A Modelica component shows its type's connectors on its icon, which is what makes a
/// diagram look wired rather than like a row of boxes, and they are separate drawings with
/// placements of their own rather than graphics in the icon's list.
/// </param>
public sealed record DiagramComponent(
    string Name, double[] Extent, double Rotation, IconData? Icon, string? TypeName = null,
    double[]? RotationCentre = null, IReadOnlyList<DiagramComponent>? Children = null);

/// <summary>One connection line, as the poly-line the diagram draws for it.</summary>
/// <param name="Points">At least two points, in the parent's diagram coordinates.</param>
/// <param name="Color">RGB as [r,g,b], or null for the default connection colour.</param>
public sealed record DiagramConnection(IReadOnlyList<double[]> Points, int[]? Color = null);

/// <summary>
/// Renders a class's diagram — its components drawn at their placements with their own icons, the
/// connection lines between them, and whatever the class draws on its own diagram layer — as one SVG.
///
/// <para><b>Why this exists.</b> An agent authoring a model through the MCP tools sets placements and
/// connections and is then told, in numbers, what it just did. It is laying out a picture it cannot
/// look at (B196). This is that picture.</para>
///
/// <para><b>It shows what falls outside the frame rather than clipping it.</b> A Modelica viewer
/// scales to the declared coordinate system and cuts off anything beyond it; here the view is the
/// union of the coordinate system and everything drawn, with the declared system outlined. A
/// component the agent placed off-canvas is the single most useful thing a picture can tell it, and
/// a viewer's behaviour would hide exactly that.</para>
/// </summary>
public static class DiagramSvgRenderer
{
    /// <summary>Modelica's default coordinate system, used when the class declares none.</summary>
    public static readonly double[] DefaultExtent = [-100, -100, 100, 100];

    private const string ConnectionColor = "#0000C8";

    /// <summary>
    /// The diagram as an SVG document.
    /// </summary>
    /// <param name="diagramLayer">The class's own <c>Diagram</c> annotation — its coordinate system
    /// and any graphics it draws itself. Null for a class with none.</param>
    /// <param name="components">The placed components, in declaration order (drawn in that order).</param>
    /// <param name="connections">The connection lines.</param>
    /// <param name="width">Width of the rendered image in pixels; the height follows the aspect ratio.</param>
    /// <param name="fileNameResolver">Resolves a Bitmap's fileName to a data URI, as for an icon.</param>
    public static string Render(
        IconData? diagramLayer,
        IReadOnlyList<DiagramComponent> components,
        IReadOnlyList<DiagramConnection> connections,
        int width = 800,
        Func<string, string?>? fileNameResolver = null)
    {
        ArgumentNullException.ThrowIfNull(components);
        ArgumentNullException.ThrowIfNull(connections);

        var declared = Normalize(diagramLayer?.CoordinateExtent ?? DefaultExtent);
        var view = Union(declared, components, connections);

        var viewWidth = view[2] - view[0];
        var viewHeight = view[3] - view[1];
        var height = (int)Math.Round(width * viewHeight / viewWidth);

        // Modelica states a line thickness and an arrow size in MILLIMETRES - a length on the page,
        // not in the drawing - so how many coordinate units they come to depends on how far in this
        // view is zoomed. A constant instead gives outlines ten pixels thick on a diagram, which is
        // what the red frames in Modelica.Blocks.Examples.PID_Controller came out as.
        var pixelsPerUnit = width / viewWidth;
        var context = ContextFor(pixelsPerUnit, scale: 1, mirrorX: false, mirrorY: false);

        var svg = new StringBuilder();
        svg.AppendLine(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{Math.Max(height, 1)}\" "
            + $"viewBox=\"{F(view[0])} {F(-view[3])} {F(viewWidth)} {F(viewHeight)}\">");
        svg.AppendLine("  <rect x=\"-100%\" y=\"-100%\" width=\"300%\" height=\"300%\" fill=\"#ffffff\"/>");
        svg.AppendLine("  <g transform=\"scale(1,-1)\">");

        AppendFrame(svg, declared, view, context);

        // The class's own diagram graphics sit under the components, as a background does.
        if (diagramLayer is { HasGraphics: true })
            Indent(svg, IconSvgRenderer.RenderPrimitives(diagramLayer.Graphics, fileNameResolver, context));

        foreach (var component in components)
            AppendComponent(svg, component, fileNameResolver, pixelsPerUnit, 1, false, false, "    ");

        // Lines last, so a connection is never hidden by a component it runs past.
        foreach (var connection in connections)
            AppendConnection(svg, connection, context);

        svg.AppendLine("  </g>");
        svg.AppendLine("</svg>");
        return svg.ToString();
    }

    /// <summary>
    /// The declared coordinate system, drawn only when something is outside it — otherwise it is the
    /// edge of the image and an outline says nothing.
    /// </summary>
    private static void AppendFrame(
        StringBuilder svg, double[] declared, double[] view, IconSvgRenderer.GraphicsContext context)
    {
        if (view.SequenceEqual(declared))
            return;

        var stroke = context.UnitsPerMillimetre * DefaultThicknessMm;
        svg.AppendLine(
            $"    <rect x=\"{F(declared[0])}\" y=\"{F(declared[1])}\" "
            + $"width=\"{F(declared[2] - declared[0])}\" height=\"{F(declared[3] - declared[1])}\" "
            + $"fill=\"none\" stroke=\"#c0c0c0\" stroke-width=\"{W(stroke)}\" "
            + $"stroke-dasharray=\"{F(stroke * 6)},{F(stroke * 4)}\"/>");
    }

    /// <summary>
    /// One component, and everything it carries, inside a transform that maps its type's icon
    /// coordinate system onto its Placement.
    /// </summary>
    /// <param name="pixelsPerUnit">Of the outermost view, for turning millimetres into units.</param>
    /// <param name="scale">How much the enclosing transforms have already scaled by, so a connector
    /// drawn two levels in still gets a one-pixel outline.</param>
    private static void AppendComponent(
        StringBuilder svg, DiagramComponent component, Func<string, string?>? fileNameResolver,
        double pixelsPerUnit, double scale, bool mirrorX, bool mirrorY, string indent)
    {
        var extent = component.Extent;
        var cx = (extent[0] + extent[2]) / 2;
        var cy = (extent[1] + extent[3]) / 2;

        if (component.Icon is not { HasGraphics: true })
        {
            AppendPlaceholder(svg, component, cx, cy, ContextFor(pixelsPerUnit, scale, mirrorX, mirrorY));
            return;
        }

        var icon = Normalize(component.Icon.CoordinateExtent);
        var iconWidth = icon[2] - icon[0];
        var iconHeight = icon[3] - icon[1];
        if (iconWidth == 0 || iconHeight == 0)
            return;

        // The placement extent is where the icon's coordinate system goes. A reversed pair gives a
        // negative scale, which is Modelica's mirroring, so it is arithmetic rather than a case.
        var scaleX = (extent[2] - extent[0]) / iconWidth;
        var scaleY = (extent[3] - extent[1]) / iconHeight;
        var iconCx = (icon[0] + icon[2]) / 2;
        var iconCy = (icon[1] + icon[3]) / 2;

        // Rotation turns about the transformation's origin, which is usually the centre of the
        // placement box and is not obliged to be. Turning about the box centre instead moves a
        // component that states an off-centre origin, rather than only turning it.
        var (rx, ry) = component.RotationCentre is { Length: >= 2 } centre
            ? (centre[0], centre[1])
            : (cx, cy);

        svg.Append(indent).Append("<g transform=\"")
            .Append($"translate({F(rx)},{F(ry)})");
        if (component.Rotation != 0)
            svg.Append($" rotate({F(component.Rotation)})");
        svg.Append($" translate({F(cx - rx)},{F(cy - ry)})")
            .Append($" scale({F(scaleX)},{F(scaleY)}) translate({F(-iconCx)},{F(-iconCy)})")
            .AppendLine("\">");

        // A non-uniform scale does not have one answer for what it does to a stroke; the geometric
        // mean is what SVG itself approximates with, and these scales are square in practice.
        var inner = scale * Math.Sqrt(Math.Abs(scaleX * scaleY));
        var innerMirrorX = mirrorX ^ (scaleX < 0);
        var innerMirrorY = mirrorY ^ (scaleY < 0);

        Indent(svg, IconSvgRenderer.RenderPrimitives(
            component.Icon.Graphics.Select(g => WithName(g, component.Name)), fileNameResolver,
            ContextFor(pixelsPerUnit, inner, innerMirrorX, innerMirrorY)), indent + "  ");

        // The connectors on the component's icon, each with a placement of its own inside it.
        foreach (var child in component.Children ?? [])
            AppendComponent(svg, child, fileNameResolver, pixelsPerUnit, inner,
                            innerMirrorX, innerMirrorY, indent + "  ");

        svg.Append(indent).AppendLine("</g>");
    }

    /// <summary>
    /// What stands in for a component whose type has no icon, or could not be resolved: its box and
    /// its name. A blank space would read as "nothing is there", which is the wrong conclusion for an
    /// agent judging its own layout — the component is there, it simply draws nothing.
    /// </summary>
    private static void AppendPlaceholder(
        StringBuilder svg, DiagramComponent component, double cx, double cy,
        IconSvgRenderer.GraphicsContext context)
    {
        var e = Normalize(component.Extent);
        var stroke = context.UnitsPerMillimetre * DefaultThicknessMm;
        svg.AppendLine(
            $"    <rect x=\"{F(e[0])}\" y=\"{F(e[1])}\" width=\"{F(e[2] - e[0])}\" height=\"{F(e[3] - e[1])}\" "
            + $"fill=\"#f5f5f5\" stroke=\"#909090\" stroke-width=\"{W(stroke)}\" "
            + $"stroke-dasharray=\"{F(stroke * 4)},{F(stroke * 3)}\"/>");
        svg.AppendLine(
            $"    <text x=\"{F(cx)}\" y=\"{F(-cy)}\" font-size=\"10\" text-anchor=\"middle\" "
            + "dominant-baseline=\"middle\" fill=\"#404040\" transform=\"scale(1,-1)\">"
            + $"{System.Security.SecurityElement.Escape(component.Name)}</text>");
    }

    private static void AppendConnection(
        StringBuilder svg, DiagramConnection connection, IconSvgRenderer.GraphicsContext context)
    {
        if (connection.Points.Count < 2)
            return;

        var points = string.Join(" ", connection.Points.Select(p => $"{F(p[0])},{F(p[1])}"));
        var color = connection.Color is { Length: >= 3 } c
            ? $"#{c[0]:X2}{c[1]:X2}{c[2]:X2}"
            : ConnectionColor;

        svg.AppendLine(
            $"    <polyline points=\"{points}\" fill=\"none\" stroke=\"{color}\" "
            + $"stroke-width=\"{W(context.UnitsPerMillimetre * DefaultThicknessMm)}\"/>");
    }

    /// <summary>
    /// A copy of the primitive with <c>%name</c> resolved, which is how every icon in the Modelica
    /// Standard Library writes a component's label. Left alone when there is nothing to substitute,
    /// so the common case allocates nothing.
    /// </summary>
    private static GraphicsPrimitive WithName(GraphicsPrimitive primitive, string name)
    {
        if (primitive is not TextPrimitive text || !text.TextString.Contains('%'))
            return primitive;

        return new TextPrimitive
        {
            Visible = text.Visible,
            Origin = text.Origin,
            Rotation = text.Rotation,
            Extent = text.Extent,
            TextString = text.TextString.Replace("%name", name, StringComparison.Ordinal),
            FontSize = text.FontSize,
            FontName = text.FontName,
            FontStyles = text.FontStyles,
            TextColor = text.TextColor,
            HorizontalAlignment = text.HorizontalAlignment,
            LineColor = text.LineColor,
            FillColor = text.FillColor,
            LinePattern = text.LinePattern,
            FillPattern = text.FillPattern,
            LineThickness = text.LineThickness,
        };
    }

    /// <summary>The view: the declared system grown to hold everything actually drawn.</summary>
    private static double[] Union(
        double[] declared,
        IReadOnlyList<DiagramComponent> components,
        IReadOnlyList<DiagramConnection> connections)
    {
        var view = (double[])declared.Clone();

        foreach (var component in components)
        {
            // A rotated component sweeps outside its own extent; its bounding circle is the cheap
            // answer and never cuts anything off.
            var e = Normalize(component.Extent);
            if (component.Rotation % 180 == 0)
            {
                Grow(view, e[0], e[1]);
                Grow(view, e[2], e[3]);
            }
            else
            {
                var r = Math.Sqrt(Math.Pow(e[2] - e[0], 2) + Math.Pow(e[3] - e[1], 2)) / 2;
                Grow(view, (e[0] + e[2]) / 2 - r, (e[1] + e[3]) / 2 - r);
                Grow(view, (e[0] + e[2]) / 2 + r, (e[1] + e[3]) / 2 + r);
            }
        }

        foreach (var point in connections.SelectMany(c => c.Points))
            Grow(view, point[0], point[1]);

        if (view.SequenceEqual(declared))
            return view;

        // A margin, so anything off-canvas is visibly off-canvas rather than flush with the edge.
        var margin = Math.Max(view[2] - view[0], view[3] - view[1]) * 0.03;
        return [view[0] - margin, view[1] - margin, view[2] + margin, view[3] + margin];
    }

    private static void Grow(double[] view, double x, double y)
    {
        view[0] = Math.Min(view[0], x);
        view[1] = Math.Min(view[1], y);
        view[2] = Math.Max(view[2], x);
        view[3] = Math.Max(view[3], y);
    }

    /// <summary>[x1,y1,x2,y2] with the lower corner first, for anything measuring or drawing a box.</summary>
    private static double[] Normalize(double[] extent) => extent.Length < 4
        ? (double[])DefaultExtent.Clone()
        : [
            Math.Min(extent[0], extent[2]), Math.Min(extent[1], extent[3]),
            Math.Max(extent[0], extent[2]), Math.Max(extent[1], extent[3]),
        ];

    private static void Indent(StringBuilder svg, string fragment, string indent = "    ")
    {
        foreach (var line in fragment.Split('\n'))
            if (line.Trim().Length > 0)
                svg.Append(indent).AppendLine(line.Trim());
    }

    /// <summary>Modelica's default line thickness, in millimetres.</summary>
    private const double DefaultThicknessMm = 0.25;

    /// <summary>
    /// The context for graphics drawn under <paramref name="scale"/>, calibrated so a line of the
    /// default 0.25 mm comes out one pixel wide however far in it is nested.
    /// </summary>
    private static IconSvgRenderer.GraphicsContext ContextFor(
        double pixelsPerUnit, double scale, bool mirrorX, bool mirrorY)
    {
        var effective = pixelsPerUnit * scale;
        return new IconSvgRenderer.GraphicsContext
        {
            UnitsPerMillimetre = effective > 0 ? 1 / (DefaultThicknessMm * effective) : IconSvgRenderer.DefaultUnitsPerMillimetre,
            MirrorX = mirrorX,
            MirrorY = mirrorY,
        };
    }

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>A stroke width, which needs more places than a coordinate does - see IconSvgRenderer.</summary>
    private static string W(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
