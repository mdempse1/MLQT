using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelicaGraph.Analysis;
using ModelicaParser.DataTypes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Diagram-layer tools: read and set the graphical Placement of components so an authored model has a
/// usable diagram rather than components stacked at the origin. Coordinates are Modelica diagram units
/// (an extent is the component's bounding box {{x1,y1},{x2,y2}}). Auto-layout is not provided — set
/// explicit placements.
/// </summary>
[McpServerToolType]
public sealed class DiagramTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public DiagramTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "mlqt_get_diagram_layout")]
    [Description("Get a Modelica class's diagram layout: each component's name, type and Placement extent " +
                "([x1,y1,x2,y2] bounding box, plus rotation if set), together with the connections - " +
                "those inside a for/if/when equation too, each with 'within' naming its branches. " +
                "INHERITED components are included, marked with the base class they come from - most " +
                "blocks declare no connector of their own and get their ports from a base class - and " +
                "so are PROTECTED ones, which are hidden from the class's users but not from its diagram. An " +
                "extent is absolute: a Placement written with an origin has it added in already. Use " +
                "this to see how a model is arranged before adjusting it, and mlqt_get_diagram_image to " +
                "look at it. Read-only.")]
    public object GetDiagramLayout(
        [Description("Fully-qualified class id.")] string classId)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse.");

        var code = node.Definition.ModelicaCode ?? string.Empty;

        // The same reader the image and the connection router use, so the numbers and the picture
        // cannot describe different diagrams - which they did while this had a regex of its own.
        var placements = DiagramGeometry.Placements(_libraries, classId, code);

        // The components the image draws, protected ones included (B315).
        var components = DiagramImage.Members(_libraries, node)
            .Select(m =>
            {
                placements.TryGetValue(m.Element.Name, out var placement);
                return new DiagramComponent(
                    m.Element.Name, m.Element.Type,
                    placement is null ? null : [.. placement.Extent.Select(ToInt)],
                    placement is null or { Rotation: 0 } ? null : ToInt(placement.Rotation),
                    m.InheritedFrom);
            })
            .ToList();

        var connections = BehaviorExtractor.ExtractFromCode(code).Connections
            .Select(ConnectionView.Of).ToList();

        return new DiagramLayoutResult(classId, components, connections);
    }

    [McpServerTool(Name = "mlqt_get_diagram_image")]
    [Description("Render a Modelica class's diagram as a PNG image and return it, so you can LOOK at a layout " +
                "rather than read its coordinates back. Each component is drawn with its own type's " +
                "icon at its Placement, with the connection lines between them; a component whose type " +
                "is not loaded is drawn as a dashed box with its name, so an unresolved type and an " +
                "absent component do not look alike. Anything placed outside the class's coordinate " +
                "system is still shown, with the declared canvas outlined - being able to see that is " +
                "most of the point. Use it after mlqt_set_component_placement / mlqt_add_connection to check what " +
                "you built: overlapping components, a signal flowing right to left and a connector left " +
                "on the wrong edge are obvious here and invisible in mlqt_get_diagram_layout. Needs only a " +
                "loaded library.")]
    public object GetDiagramImage(
        [Description("Fully-qualified class id.")] string classId,
        [Description("Image width in pixels (default 800, 200-2000). The height follows the diagram's " +
                     "aspect ratio.")]
        int width = 800)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse.");

        width = Math.Clamp(width, DiagramImage.MinWidth, DiagramImage.MaxWidth);

        string? svg;
        try
        {
            svg = DiagramImage.RenderSvg(_libraries, node, width);
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not draw the diagram of '{classId}': {ex.Message}");
        }

        if (svg is null)
            return new ToolError(
                $"'{classId}' has nothing to draw: no component carries a Placement and the class has no "
                + "diagram graphics of its own. Use mlqt_set_component_placement to position its components.");

        try
        {
            return new ImageContentBlock
            {
                MimeType = "image/png",
                // Data is the base64 as UTF-8 bytes, which is what goes on the wire.
                Data = System.Text.Encoding.UTF8.GetBytes(Convert.ToBase64String(DiagramImage.ToPng(svg))),
            };
        }
        catch (Exception ex)
        {
            return new ToolError($"Could not rasterise the diagram of '{classId}': {ex.Message}");
        }
    }

    [McpServerTool(Name = "mlqt_set_component_placement")]
    [Description("Set (or replace) a component's diagram Placement in a Modelica class so it appears at a given position. " +
                "Provide the component name and its bounding extent x1,y1,x2,y2 (diagram units, e.g. " +
                "-10,-10,10,10) and an optional rotation. Adds a Placement annotation if the component has " +
                "none; otherwise replaces only its transformation (the diagram position), keeping its " +
                "iconTransformation (where it sits on the class's icon) and visible. Any connection to this component whose other end is " +
                "also placed automatically gets (or has refreshed) an orthogonal diagram Line routed between " +
                "the connector positions, so positioned components appear wired up — no separate call " +
                "needed. A connect inside a for/if/when equation is left as written. Fails if the " +
                "component doesn't exist or the result would not parse. Set " +
                "preview=true to see the file text.")]
    public async Task<object> SetComponentPlacement(
        [Description("Fully-qualified class id containing the component.")] string classId,
        [Description("The component's name.")] string componentName,
        [Description("Extent x1 (left).")] int x1,
        [Description("Extent y1 (bottom).")] int y1,
        [Description("Extent x2 (right).")] int x2,
        [Description("Extent y2 (top).")] int y2,
        [Description("Rotation in degrees (default 0).")] int rotation = 0,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var newClassCode = SetPlacement(ctx!.ClassCode, componentName, x1, y1, x2, y2, rotation);
        if (newClassCode is null)
            return new ToolError($"'{classId}' has no component named '{componentName}'.");

        // Now that this component is positioned, add/refresh the diagram lines of connections to it (and any
        // other connection whose endpoints are both placed) so it appears wired up without a further call.
        newClassCode = ConnectionLineAnnotator.Annotate(_libraries, classId, newClassCode);

        var result = await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"set placement in '{classId}'");
        if (result is ToolError)
            return result;
        var r = (ClassEditResult)result;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, null);
    }

    private static int ToInt(double value) => (int)Math.Round(value);

    // Set the Placement on a component, returning the new class code, or null if the component is absent.
    private static string? SetPlacement(string classCode, string componentName, int x1, int y1, int x2, int y2, int rotation)
    {
        var decl = ModelicaNav.FindComponent(classCode, componentName);
        if (decl is null)
            return null;

        var extent = "{{" + x1 + "," + y1 + "},{" + x2 + "," + y2 + "}}";
        var rot = rotation != 0 ? ", rotation=" + rotation : string.Empty;
        var transformation = "transformation(extent=" + extent + rot + ")";
        var placement = "Placement(" + transformation + ")";

        var annotation = decl.comment()?.annotation();
        if (annotation is not null)
        {
            var existing = FindPlacementArgument(annotation);
            if (existing is not null)
                return ReplaceTransformation(classCode, existing, transformation)
                       ?? classCode[..existing.Start.StartIndex] + placement + classCode[(existing.Stop.StopIndex + 1)..];

            // Annotation exists but no Placement: insert as the first argument.
            var cm = annotation.class_modification();
            var at = cm.Start.StartIndex + 1; // just after '('
            var hasArgs = cm.argument_list()?.argument().Length > 0;
            var insert = hasArgs ? placement + ", " : placement;
            return classCode[..at] + insert + classCode[at..];
        }

        // No annotation at all: add one after the declaration (before the terminating ';').
        var end = decl.Stop.StopIndex + 1;
        return classCode[..end] + " annotation (" + placement + ")" + classCode[end..];
    }

    private static modelicaParser.ArgumentContext? FindPlacementArgument(modelicaParser.AnnotationContext annotation)
        => FindArgument(annotation.class_modification(), "Placement");

    private static modelicaParser.ArgumentContext? FindArgument(
        modelicaParser.Class_modificationContext? modification, string name)
    {
        var argList = modification?.argument_list();
        if (argList is null)
            return null;
        foreach (var arg in argList.argument())
        {
            if (arg.element_modification_or_replaceable()?.element_modification()?.name()?.GetText() == name)
                return arg;
        }
        return null;
    }

    /// <summary>
    /// The class code with only the <c>transformation(...)</c> of an existing Placement replaced -
    /// or added as its first argument when it has none - or null when the Placement has no argument
    /// list to edit.
    ///
    /// <para><b>Only the transformation</b> (B321). A Placement also carries
    /// <c>iconTransformation</c>, where the component sits on the enclosing class's <i>icon</i>,
    /// and <c>visible</c>. Overwriting the whole Placement deleted both, so moving a class's own
    /// connector on its diagram moved it on the class's icon too, in every diagram that uses the
    /// class. The old transformation's <c>origin</c> and <c>rotation</c> do go: the extent this
    /// writes is absolute.</para>
    /// </summary>
    private static string? ReplaceTransformation(
        string classCode, modelicaParser.ArgumentContext placement, string transformation)
    {
        var arguments = placement.element_modification_or_replaceable()?.element_modification()
            ?.modification()?.class_modification();
        if (arguments?.argument_list() is null)
            return null;

        if (FindArgument(arguments, "transformation") is { } existing)
            return classCode[..existing.Start.StartIndex] + transformation + classCode[(existing.Stop.StopIndex + 1)..];

        var at = arguments.Start.StartIndex + 1; // just after '('
        return classCode[..at] + transformation + ", " + classCode[at..];
    }
}
