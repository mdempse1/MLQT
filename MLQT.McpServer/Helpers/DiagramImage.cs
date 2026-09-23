using System.Globalization;
using System.Text.RegularExpressions;
using ModelicaGraph.Analysis;
using ModelicaGraph.DataTypes;
using ModelicaParser;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Icons;
using ModelicaParser.Visitors;
using MLQT.Services.Interfaces;
using SkiaSharp;
using Svg.Skia;

namespace MLQT.McpServer.Helpers;

/// <summary>
/// Draws a class's diagram — its components at their placements, wearing their own types' icons,
/// with the connection lines between them — and rasterises it so an agent can look at it (B196).
///
/// <para><b>Why the tools needed this.</b> <c>set_component_placement</c> and <c>add_connection</c>
/// let an agent lay out a model and then tell it, in coordinates, what it just did. Reading numbers
/// back is not seeing the picture: components overlapping, a signal flowing right to left, a
/// connector left on the wrong edge and a component put outside the canvas are all obvious in an
/// image and invisible in a list of extents.</para>
///
/// <para><b>It lives here rather than in a service</b> because everything it composes with is here:
/// the placement reader and the connection router in <see cref="DiagramGeometry"/>, which the
/// diagram tools already share. The desktop app does not want a PNG — it has a browser — so the
/// Skia dependency stays in this one project.</para>
/// </summary>
internal static class DiagramImage
{
    /// <summary>Widths outside this are refused: below it nothing is legible, above it is only bytes.</summary>
    public const int MinWidth = 200;
    public const int MaxWidth = 2000;

    private static readonly Regex PointRegex = new(
        @"\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}", RegexOptions.Compiled);
    private static readonly Regex ColorRegex = new(
        @"color\s*=\s*\{\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\}", RegexOptions.Compiled);

    /// <summary>The diagram as SVG, or null when the class has nothing to draw.</summary>
    public static string? RenderSvg(ILibraryDataService libraries, ModelNode node, int width)
    {
        var code = node.Definition.ModelicaCode ?? string.Empty;
        var tree = node.Definition.EnsureParsed();
        if (tree is null)
            return null;

        var placements = DiagramGeometry.Placements(libraries, node.Id, code);
        var components = Components(libraries, node, placements);
        var connections = Connections(libraries, node.Id, code, placements);
        var diagramLayer = IconExtractor.ExtractDiagram(tree);

        if (components.Count == 0 && connections.Count == 0 && diagramLayer is not { HasGraphics: true })
            return null;

        return DiagramSvgRenderer.Render(diagramLayer, components, connections, width);
    }

    /// <summary>The SVG rasterised to PNG bytes.</summary>
    public static byte[] ToPng(string svg)
    {
        using var document = new SKSvg();
        var picture = document.FromSvg(svg)
            ?? throw new InvalidOperationException("the rendered diagram SVG could not be read back");

        // The SVG already carries the pixel size asked for, so the picture's own bounds are it.
        var bounds = picture.CullRect;
        var info = new SKImageInfo(
            Math.Max((int)Math.Ceiling(bounds.Width), 1), Math.Max((int)Math.Ceiling(bounds.Height), 1));

        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.DrawPicture(picture);
        surface.Canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    // --- Components ------------------------------------------------------------------------------

    /// <summary>
    /// The components to draw: everything the class presents, declared or inherited, that carries a
    /// Placement. The set comes from <c>ClassElementResolver</c> rather than from the class's own
    /// text, because most blocks in the Modelica Standard Library declare no connector at all - a
    /// diagram built from the declarations alone leaves out the two ports every reader looks for.
    /// </summary>
    private static List<DiagramComponent> Components(
        ILibraryDataService libraries, ModelNode node,
        IReadOnlyDictionary<string, DiagramGeometry.Placement> placements)
    {
        var imports = Imports(node);
        var components = new List<DiagramComponent>();

        foreach (var member in ClassElementResolver
                     .Collect(libraries.CombinedGraph, node, includeProtected: false, includeInherited: true)
                     .Where(m => m.Element.Kind == ClassElementKind.Component))
        {
            // No Placement means Modelica does not draw it either - a parameter, or a component the
            // author never put on the diagram.
            if (!placements.TryGetValue(member.Element.Name, out var placement))
                continue;

            // Resolved in the scope of the class that DECLARED it, which for an inherited connector
            // is the base class and not this one.
            var scope = member.InheritedFrom is null ? imports : member.OwnerImports;
            var type = TypeResolver.Resolve(
                libraries.CombinedGraph, member.OwnerId, member.Element.Type, scope);

            // What this instance's parameters are: the modification it was given, then the type's
            // own default. It answers both the %references in the icon's text (B278) and whether a
            // conditional connector on that icon is there at all (B277).
            var valueOf = ComponentValues.For(
                libraries.CombinedGraph, type, member.Element.Modifications);

            components.Add(new DiagramComponent(
                member.Element.Name, placement.Extent, placement.Rotation,
                IconOf(libraries, member.OwnerId, member.Element.Type, scope),
                member.Element.Type,
                placement.RotationCentre,
                type is null ? null : ConnectorsOn(libraries, type, valueOf),
                valueOf));
        }

        return components;
    }

    /// <summary>
    /// What a component's type draws when it is shown on someone else's diagram. Null when the type
    /// is not loaded - the renderer draws an outline for that, because "MLQT cannot resolve this
    /// type" and "this component is not there" must not look the same.
    ///
    /// <para><b>A connector is the exception, and it is the whole of what a block's diagram shows.</b>
    /// Modelica gives a connector two representations: the icon layer, used where the connector
    /// appears on the enclosing class's own icon, and the diagram layer, used where it appears on
    /// the enclosing class's diagram. They are different drawings - <c>RealInput</c>'s icon is a
    /// triangle filling its whole coordinate system, its diagram layer is a smaller triangle sitting
    /// against the edge plus a <c>%name</c> label - so drawing the icon in a diagram gives a
    /// connector several times the size Dymola draws, and no name beside it.</para>
    /// </summary>
    private static IconData? IconOf(
        ILibraryDataService libraries, string classId, string? typeText, IReadOnlyList<string> imports)
    {
        if (string.IsNullOrWhiteSpace(typeText))
            return null;

        var type = TypeResolver.Resolve(libraries.CombinedGraph, classId, typeText, imports);
        if (type?.Definition.ModelicaCode is not { Length: > 0 } source)
            return null;

        if (string.Equals(type.ClassType, "connector", StringComparison.Ordinal)
            && DiagramLayerOf(type) is { } diagram)
            return diagram;

        var dot = type.Id.LastIndexOf('.');
        return IconSvgRenderer.ExtractIconWithInheritance(
            source,
            baseName => Resolve(libraries, type.Id, baseName)?.Definition.ModelicaCode,
            initialPackageContext: dot > 0 ? type.Id[..dot] : null);
    }

    /// <summary>
    /// The connectors a component shows on its icon, in the icon's own coordinates.
    ///
    /// <para><b>Without these a diagram is a row of boxes with lines ending near them.</b> A block's
    /// icon graphics draw the block; its ports are separate components of the type, each with a
    /// placement of its own, and Modelica puts them on the icon so a reader can see what connects to
    /// what.</para>
    ///
    /// <para><b>Where they go is the icon layer's answer, which usually is not written down.</b> A
    /// Placement may carry an <c>iconTransformation</c> saying where the connector sits on the
    /// enclosing class's icon; where it does not — which is most of the Modelica Standard Library —
    /// the <c>transformation</c> serves for both. And it is the connector's ICON layer that is drawn
    /// here, not its diagram layer: this is an icon.</para>
    /// </summary>
    private static List<DiagramComponent>? ConnectorsOn(
        ILibraryDataService libraries, ModelNode type, Func<string, string?> valueOf)
    {
        var code = type.Definition.ModelicaCode;
        if (string.IsNullOrEmpty(code))
            return null;

        var placements = DiagramGeometry.Placements(
            libraries, type.Id, code, DiagramGeometry.Layer.Icon);
        if (placements.Count == 0)
            return null;

        var connectors = new List<DiagramComponent>();

        foreach (var member in ClassElementResolver
                     .Collect(libraries.CombinedGraph, type, includeProtected: false, includeInherited: true)
                     .Where(m => m.Element.Kind == ClassElementKind.Component))
        {
            if (!placements.TryGetValue(member.Element.Name, out var placement))
                continue;

            // A connector declared `if <expr>` is only there when the expression holds, and an
            // instance that never enabled it has no such port: MSL's Integrator, Torque and
            // SpringDamper each carry one that is off by default, and drawing them put three ports
            // on a diagram that Dymola leaves bare (B277). Undecidable answers are drawn — showing a
            // port that is switched off is a smaller lie than hiding one that is switched on.
            if (ModelicaCondition.Evaluate(member.Element.Condition, valueOf) == false)
                continue;

            var memberType = TypeResolver.Resolve(
                libraries.CombinedGraph, member.OwnerId, member.Element.Type, member.OwnerImports);

            // Only connectors. A block that contains other blocks does not draw them on its icon —
            // that is its diagram, and it is a different picture.
            if (memberType is null || !string.Equals(memberType.ClassType, "connector", StringComparison.Ordinal))
                continue;

            connectors.Add(new DiagramComponent(
                member.Element.Name, placement.Extent, placement.Rotation,
                IconWithInheritance(libraries, memberType),
                member.Element.Type,
                placement.RotationCentre));
        }

        return connectors.Count > 0 ? connectors : null;
    }

    private static IconData? IconWithInheritance(ILibraryDataService libraries, ModelNode type)
    {
        if (type.Definition.ModelicaCode is not { Length: > 0 } source)
            return null;

        var dot = type.Id.LastIndexOf('.');
        return IconSvgRenderer.ExtractIconWithInheritance(
            source,
            baseName => Resolve(libraries, type.Id, baseName)?.Definition.ModelicaCode,
            initialPackageContext: dot > 0 ? type.Id[..dot] : null);
    }

    /// <summary>
    /// A connector's own diagram layer, or null when it has none and the icon layer must stand in.
    /// Not merged down the extends chain: a diagram is what a class draws itself, where an icon is
    /// composed from what it inherits.
    /// </summary>
    private static IconData? DiagramLayerOf(ModelNode type)
    {
        var tree = type.Definition.EnsureParsed();
        var diagram = tree is null ? null : IconExtractor.ExtractDiagram(tree);
        return diagram is { HasGraphics: true } ? diagram : null;
    }

    private static ModelNode? Resolve(ILibraryDataService libraries, string fromId, string name)
        => TypeResolver.Resolve(libraries.CombinedGraph, fromId, name, []);

    private static IReadOnlyList<string> Imports(ModelNode node)
    {
        var tree = node.Definition.EnsureParsed();
        if (tree is null)
            return [];
        return [.. ClassInterfaceExtractor.Extract(tree).Elements
            .Where(e => e.Kind == ClassElementKind.Import)
            .Select(e => e.Name)];
    }

    // --- Connections -----------------------------------------------------------------------------

    /// <summary>
    /// One poly-line per connection. The route the class already carries is used where there is one,
    /// because that is what the diagram actually looks like; a connection with no <c>Line</c>
    /// annotation is routed the same way <c>add_connection</c> would route it, so a model assembled
    /// by an agent draws before it has been annotated.
    /// </summary>
    private static List<DiagramConnection> Connections(
        ILibraryDataService libraries, string classId, string code,
        IReadOnlyDictionary<string, DiagramGeometry.Placement> placements)
    {
        var connections = new List<DiagramConnection>();

        var composition = ModelicaParserHelper.Parse(code)?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.children is null)
            return connections;

        foreach (var section in composition.children.OfType<modelicaParser.Equation_sectionContext>())
        foreach (var equation in section.equation_or_comment().Select(e => e.equation()))
        {
            if (equation?.connect_clause() is not { } connect)
                continue;
            var refs = connect.component_reference();
            if (refs.Length < 2)
                continue;

            var annotated = FromAnnotation(code, equation);
            if (annotated is not null)
            {
                connections.Add(annotated);
                continue;
            }

            var route = DiagramGeometry.RouteConnection(
                libraries, classId, code, refs[0].GetText(), refs[1].GetText());
            if (route is { Count: >= 2 })
                connections.Add(new DiagramConnection(
                    [.. route.Select(p => new[] { p.X, p.Y })],
                    ConnectorColor.Resolve(libraries, classId, refs[0].GetText()) is { } literal
                        ? ParseColor(literal)
                        : null));
        }

        return connections;
    }

    /// <summary>The <c>Line(points=…, color=…)</c> the connect already carries, or null.</summary>
    private static DiagramConnection? FromAnnotation(string code, modelicaParser.EquationContext equation)
    {
        var annotation = equation.comment()?.annotation();
        var arguments = annotation?.class_modification()?.argument_list()?.argument();
        if (arguments is null)
            return null;

        foreach (var argument in arguments)
        {
            var modification = argument.element_modification_or_replaceable()?.element_modification();
            if (!string.Equals(modification?.name()?.GetText(), "Line", StringComparison.Ordinal))
                continue;
            if (modification!.Start is null || modification.Stop is null)
                continue;

            var text = code[modification.Start.StartIndex..(modification.Stop.StopIndex + 1)];
            if (PointsArray(text) is not { } list)
                return null;

            var parsed = PointRegex.Matches(list)
                .Select(m => new[] { Num(m.Groups[1].Value), Num(m.Groups[2].Value) })
                .ToList();
            if (parsed.Count < 2)
                return null;

            var color = ColorRegex.Match(text);
            return new DiagramConnection(parsed, color.Success
                ? [int.Parse(color.Groups[1].Value), int.Parse(color.Groups[2].Value), int.Parse(color.Groups[3].Value)]
                : null);
        }

        return null;
    }

    /// <summary>A Modelica colour literal, e.g. <c>{0,0,127}</c>, as RGB.</summary>
    /// <summary>
    /// The contents of a <c>points={{..},{..},..}</c> array, matched by brace depth.
    ///
    /// <para>The obvious regex is <c>points=\{(.*?)\}\s*\}</c>, and it is wrong in a way that looks
    /// right: lazily, the first <c>}}</c> it can reach is the one closing the <em>last</em> point, so
    /// the array comes back with its final point cut in half. A two-point connection then had one
    /// point and was silently re-routed, and a six-point one was drawn with five — the connection
    /// lines that stopped short of what they connect.</para>
    /// </summary>
    private static string? PointsArray(string text)
    {
        var at = text.IndexOf("points", StringComparison.Ordinal);
        if (at < 0)
            return null;

        var open = text.IndexOf('{', at);
        if (open < 0)
            return null;

        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{')
                depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[(open + 1)..i];
        }

        return null;
    }

    private static int[]? ParseColor(string literal)
    {
        var all = Regex.Matches(literal, @"\d+");
        return all.Count >= 3
            ? [int.Parse(all[0].Value), int.Parse(all[1].Value), int.Parse(all[2].Value)]
            : null;
    }

    private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
