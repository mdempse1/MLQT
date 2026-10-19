using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Two tools of the consolidated set (B503): <c>mlqt_get_class</c>, every read of a class in one call,
/// and <c>mlqt_refactor_class</c>, its rename, move and delete. Each is a facade over the current tools
/// it replaces, which it calls rather than repeats.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class ClassFacadeTools
{
    /// <summary>The most classes one <c>mlqt_get_class</c> call reads: their sources can be long.</summary>
    internal const int MaxClasses = 20;

    /// <summary>The views, in the order they are listed; <c>info</c> and <c>source</c> are the default.</summary>
    internal static readonly IReadOnlyList<string> Views =
        ["info", "source", "interface", "elements", "behavior", "connections", "documentation", "layout", "resources"];

    private readonly ILibraryDataService _libraries;
    private readonly ClassQueryTools _classes;
    private readonly ViewTools _views;
    private readonly StructureEditTools _structure;
    private readonly DiagramTools _diagram;
    private readonly ResourceTools _resources;
    private readonly EditTools _edit;
    private readonly DependencyTools _dependencies;
    private readonly SessionState _session;

    public ClassFacadeTools(ILibraryDataService libraries, IExternalResourceService resources,
        IImpactAnalysisService impact, SessionState session)
    {
        _libraries = libraries;
        _session = session;
        _dependencies = new DependencyTools(libraries, impact, resources, session);
        _classes = new ClassQueryTools(libraries);
        _views = new ViewTools(libraries);
        _structure = new StructureEditTools(libraries, resources, session);
        _diagram = new DiagramTools(libraries, resources, session);
        _resources = new ResourceTools(libraries, resources, session);
        _edit = new EditTools(libraries, resources, session);
    }

    [McpServerTool(Name = "mlqt_get_class")]
    [Description("Read one or more Modelica classes by full id, in the views asked for (default info and " +
                 "source). Views: 'info' (kind, file, description, parent and children), 'source' (the code; " +
                 "annotations hidden unless includeAnnotations=true - copy edit text with them shown), " +
                 "'interface' (how to use it: parameters, connectors, extends, inherited members included), " +
                 "'elements' (every declaration, with defaults, modifications and comments), 'behavior' " +
                 "(equations and algorithms), 'connections' (its connect equations), 'documentation' (its Info " +
                 "and revisions), 'layout' (component placements on the diagram) and 'resources' (the files it " +
                 "loads). Each class answers each view as the view's own tool would, an error included; an " +
                 "unknown class answers once, with the nearest ids. At most 20 classes.")]
    public async Task<object> GetClass(
        [Description("One or more fully-qualified class ids, e.g. ['Modelica.Blocks.Continuous.Integrator'].")]
        IReadOnlyList<string> classIds,
        [Description("Which views, e.g. ['interface'] or ['source', 'connections']. Default ['info', 'source'].")]
        IReadOnlyList<string>? views = null,
        [Description("For 'source': include annotations. Default false.")] bool includeAnnotations = false,
        [Description("For 'interface' and 'elements': include inherited members. Default true.")] bool includeInherited = true,
        [Description("For 'elements': include protected ones. Default false.")] bool includeProtected = false,
        [Description("For 'documentation': 'text' (default) or 'html'.")] string documentationFormat = "text")
    {
        if (classIds is null || classIds.Count == 0)
            return new ToolError("classIds must name at least one class.");
        if (classIds.Count > MaxClasses)
            return new ToolError($"At most {MaxClasses} classes in one call; {classIds.Count} were asked for. Split the call.");

        var wanted = (views is { Count: > 0 } ? views : ["info", "source"])
            .Select(v => v.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.FirstOrDefault(v => !Views.Contains(v)) is { } unknown)
            return new ToolError($"'{unknown}' is not a view. Views: {string.Join(", ", Views)}.");

        // The resources view reads the resource index, which the analysis builds; the consolidated set has
        // no tool to run it, so the view runs it (B503).
        if (wanted.Contains("resources") && await AnalyseFirstAsync() is { } notAnalysed)
            return notAnalysed;

        var classes = new List<ClassViews>();
        foreach (var classId in classIds)
        {
            if (_libraries.GetModelById(classId) is null)
            {
                classes.Add(new ClassViews(classId, Error: ToolDiagnostics.ClassNotFound(_libraries, classId).Error));
                continue;
            }

            object? View(string name, Func<object> read) => wanted.Contains(name) ? read() : null;
            classes.Add(new ClassViews(classId,
                Info: View("info", () => _classes.GetClassInfo(classId)),
                Source: View("source", () => _classes.GetClassSource(classId, includeAnnotations)),
                Interface: View("interface", () => _views.GetClassInterface(classId, includeInherited)),
                Elements: View("elements", () => _views.ListClassElements(classId, includeProtected, includeInherited)),
                Behavior: View("behavior", () => _views.GetClassBehavior(classId)),
                Connections: View("connections", () => _structure.ListConnections(classId)),
                Documentation: View("documentation", () => _views.GetClassDocumentation(classId, documentationFormat)),
                Layout: View("layout", () => _diagram.GetDiagramLayout(classId)),
                Resources: View("resources", () => _resources.GetClassResources(classId))));
        }
        return new GetClassResult(classes);
    }

    [McpServerTool(Name = "mlqt_refactor_class")]
    [Description("Rename, move or delete a Modelica class. action 'rename' takes newName (the new short name) " +
                 "and 'move' takes newParentId (the package it goes into); both rewrite every reference to it " +
                 "across the loaded libraries, and refuse a change that would make a reference mean something " +
                 "else. 'delete' removes the class and its storage, and lists the classes still referring to it " +
                 "once dependencies are analysed - those references are not rewritten. Every file is " +
                 "parse-checked before it is written; preview=true returns what would change without writing.")]
    public async Task<object> RefactorClass(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("'rename', 'move' or 'delete'.")] string action,
        [Description("For 'rename': the new short name, not a full id.")] string? newName = null,
        [Description("For 'move': the full id of the package to move it into.")] string? newParentId = null,
        [Description("Return what would change without writing. Default false.")] bool preview = false)
    {
        // First, as every editing tool does: a read-only class is refused whatever was asked of it.
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "rename, move or delete this class") is { } readOnlyClass)
            return readOnlyClass;

        // Each finds the classes to rewrite, or to report, from the dependency graph; the consolidated set
        // has no tool to build it, so this does (B503).
        if (action?.Trim().ToLowerInvariant() is "rename" or "move" or "delete" && await AnalyseFirstAsync() is { } notAnalysed)
            return notAnalysed;

        switch (action?.Trim().ToLowerInvariant())
        {
            case "rename":
                return string.IsNullOrWhiteSpace(newName)
                    ? new ToolError("'rename' needs newName: the class's new short name.")
                    : await _edit.RenameClass(classId, newName, preview);
            case "move":
                return string.IsNullOrWhiteSpace(newParentId)
                    ? new ToolError("'move' needs newParentId: the full id of the package to move the class into.")
                    : await _edit.MoveClass(classId, newParentId, preview);
            case "delete":
                return await _edit.DeleteClass(classId, preview);
            default:
                return new ToolError($"action must be 'rename', 'move' or 'delete', not '{action}'.");
        }
    }

    /// <summary>Runs the dependency and resource analysis when the session has not, as
    /// <c>mlqt_analyze_dependencies</c> would; null when it has run, else its error.</summary>
    private async Task<ToolError?> AnalyseFirstAsync()
    {
        if (_session.DependenciesAnalyzed)
            return null;
        return await _dependencies.AnalyzeDependencies() as ToolError;
    }
}
