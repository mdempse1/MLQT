using System.ComponentModel;
using System.Text.RegularExpressions;
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
    private static readonly Regex ExtentRegex =
        new(@"extent\s*=\s*\{\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}\s*,\s*\{\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\}\s*\}",
            RegexOptions.Compiled);
    private static readonly Regex RotationRegex = new(@"rotation\s*=\s*(-?\d+)", RegexOptions.Compiled);

    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public DiagramTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "get_diagram_layout")]
    [Description("Get a class's diagram layout: each component's name, type and Placement extent " +
                "([x1,y1,x2,y2] bounding box, plus rotation if set), together with the connections. Use " +
                "this to see how a model is arranged before adjusting it. Read-only.")]
    public object GetDiagramLayout(
        [Description("Fully-qualified class id.")] string classId)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse.");

        var code = node.Definition.ModelicaCode ?? string.Empty;
        var layout = ClassBodyLocator.Analyze(code);

        var components = layout.Components.Select(c =>
        {
            var text = code[c.DeclStart..(c.DeclStop + 1)];
            var (extent, rotation) = ParsePlacement(text);
            return new DiagramComponent(c.Name, c.TypeText, extent, rotation);
        }).ToList();

        var connections = BehaviorExtractor.ExtractFromCode(code).Connections
            .Select(x => new ConnectionView(x.PortA, x.PortB)).ToList();

        return new DiagramLayoutResult(classId, components, connections);
    }

    [McpServerTool(Name = "set_component_placement")]
    [Description("Set (or replace) a component's diagram Placement so it appears at a given position. " +
                "Provide the component name and its bounding extent x1,y1,x2,y2 (diagram units, e.g. " +
                "-10,-10,10,10) and an optional rotation. Adds a Placement annotation if the component has " +
                "none, or replaces the existing one. Fails if the component doesn't exist or the result " +
                "would not parse. Set preview=true to see the file text.")]
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

        var result = await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"set placement in '{classId}'");
        if (result is ToolError)
            return result;
        var r = (ClassEditResult)result;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, null);
    }

    private static (IReadOnlyList<int>? Extent, int? Rotation) ParsePlacement(string componentText)
    {
        var m = ExtentRegex.Match(componentText);
        IReadOnlyList<int>? extent = null;
        if (m.Success)
            extent = new[] { ToInt(m.Groups[1].Value), ToInt(m.Groups[2].Value), ToInt(m.Groups[3].Value), ToInt(m.Groups[4].Value) };
        var rot = RotationRegex.Match(componentText);
        int? rotation = rot.Success ? ToInt(rot.Groups[1].Value) : null;
        return (extent, rotation);
    }

    private static int ToInt(string s) => (int)Math.Round(double.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

    // Set the Placement on a component, returning the new class code, or null if the component is absent.
    private static string? SetPlacement(string classCode, string componentName, int x1, int y1, int x2, int y2, int rotation)
    {
        var decl = FindComponentDeclaration(classCode, componentName);
        if (decl is null)
            return null;

        var extent = "{{" + x1 + "," + y1 + "},{" + x2 + "," + y2 + "}}";
        var rot = rotation != 0 ? ", rotation=" + rotation : string.Empty;
        var placement = "Placement(transformation(extent=" + extent + rot + "))";

        var annotation = decl.comment()?.annotation();
        if (annotation is not null)
        {
            var existing = FindPlacementArgument(annotation);
            if (existing is not null)
                return classCode[..existing.Start.StartIndex] + placement + classCode[(existing.Stop.StopIndex + 1)..];

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

    private static modelicaParser.Component_declarationContext? FindComponentDeclaration(string classCode, string name)
    {
        var composition = ModelicaParserHelper.Parse(classCode)
            ?.class_definition()?.FirstOrDefault()
            ?.class_specifier()?.long_class_specifier()?.composition();
        if (composition?.element_list() is not { } lists)
            return null;

        foreach (var list in lists)
            foreach (var element in list.element())
            {
                var componentList = element.component_clause()?.component_list();
                if (componentList is null)
                    continue;
                foreach (var decl in componentList.component_declaration())
                    if (decl.declaration()?.IDENT()?.GetText() == name)
                        return decl;
            }
        return null;
    }

    private static modelicaParser.ArgumentContext? FindPlacementArgument(modelicaParser.AnnotationContext annotation)
    {
        var argList = annotation.class_modification()?.argument_list();
        if (argList is null)
            return null;
        foreach (var arg in argList.argument())
        {
            var name = arg.element_modification_or_replaceable()?.element_modification()?.name()?.GetText();
            if (name == "Placement")
                return arg;
        }
        return null;
    }
}
