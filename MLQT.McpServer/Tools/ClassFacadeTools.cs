using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
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

    [McpServerTool(Name = "mlqt_get_class", ReadOnly = true, OpenWorld = false)]
    [Description("Read one or more Modelica classes by full id - use this rather than cat, grep or reading the " +
                 ".mo files, however small the library: a file may hold many classes and their annotations, and " +
                 "this answers with the class alone. Ask for every class a change runs through in one call. " +
                 "Answers the views asked for (default info and source). Views:'info' (kind, file, description, parent and children), 'source' (the code; " +
                 "annotations hidden unless includeAnnotations=true - copy edit text with them shown), " +
                 "'interface' (how to use it: parameters, connectors, extends, inherited members included), " +
                 "'elements' (every declaration, with defaults, modifications and comments), 'behavior' " +
                 "(equations and algorithms), 'connections' (its connect equations), 'documentation' (its Info " +
                 "and revisions), 'layout' (component placements on the diagram) and 'resources' (the files it " +
                 "loads). Each class answers each view as the view's own tool would, an error included; an " +
                 "unknown class answers once, with the nearest ids. At most 20 classes. An answer is kept to about " +
                 "40,000 characters: a class that does not fit says so in 'truncated', and a long source is cut at a " +
                 "line, with the sourceFromLine to ask for next.")]
    public async Task<object> GetClass(
        [Description("One or more fully-qualified class ids, e.g. ['Modelica.Blocks.Continuous.Integrator'].")]
        IReadOnlyList<string> classIds,
        [Description("Which views, e.g. ['interface'] or ['source', 'connections']. Default ['info', 'source'].")]
        IReadOnlyList<string>? views = null,
        [Description("For 'source': include annotations. Default false.")] bool includeAnnotations = false,
        [Description("For 'interface' and 'elements': include inherited members. Default true.")] bool includeInherited = true,
        [Description("For 'elements': include protected ones. Default false.")] bool includeProtected = false,
        [Description("For 'documentation': 'text' (default) or 'html'.")] string documentationFormat = "text",
        [Description("For 'source': the line to start from, when an earlier answer cut a long source short; it applies " +
                     "to every class named, so ask for that class alone. Default 1.")]
        int sourceFromLine = 1)
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
                Source: View("source", () => FromLine(_classes.GetClassSource(classId, includeAnnotations), sourceFromLine)),
                Interface: View("interface", () => _views.GetClassInterface(classId, includeInherited)),
                Elements: View("elements", () => _views.ListClassElements(classId, includeProtected, includeInherited)),
                Behavior: View("behavior", () => _views.GetClassBehavior(classId)),
                Connections: View("connections", () => _structure.ListConnections(classId)),
                Documentation: View("documentation", () => _views.GetClassDocumentation(classId, documentationFormat)),
                Layout: View("layout", () => _diagram.GetDiagramLayout(classId)),
                Resources: View("resources", () => _resources.GetClassResources(classId))));
        }
        return new GetClassResult(WithinBudget(classes, sourceFromLine, MaxAnswerCharacters));
    }

    /// <summary>
    /// The most characters of JSON one <c>mlqt_get_class</c> answer holds. A client caps what a tool may
    /// return - Claude Code at 25,000 tokens - and saves a longer answer to a file instead, which the agent
    /// then has to read with the shell: five classes, one a large parameter record, came to
    /// 63,000 characters and went to a file (a 2026-11 session). JSON of numbers and escaped source runs
    /// at about two characters a token, so this keeps well inside that.
    /// </summary>
    internal const int MaxAnswerCharacters = 40_000;

    /// <summary>The source view from <paramref name="fromLine"/> on, its numbering kept: the lines
    /// before it are left out, not renumbered, so a line number still means the class's line.</summary>
    private static object FromLine(object source, int fromLine)
    {
        if (fromLine <= 1 || source is not ClassSourceResult result)
            return source;
        var lines = result.Source.Split('\n');
        return result with { Source = string.Join("\n", lines.Skip(Math.Min(fromLine - 1, lines.Length))) };
    }

    /// <summary>
    /// <paramref name="classes"/> cut to <paramref name="budget"/> characters of JSON, in order: each class
    /// that fits is answered whole; the first that does not has its source cut at a line, when that leaves
    /// room for it, and says which line to ask from next; every class after that is named, with how to
    /// ask for it, and nothing else. Nothing is left out without saying so.
    /// </summary>
    internal static IReadOnlyList<ClassViews> WithinBudget(IReadOnlyList<ClassViews> classes, int sourceFromLine, int budget)
    {
        static int Size(ClassViews c) => JsonSerializer.Serialize(c, McpServerJson.Options).Length;

        var answer = new List<ClassViews>();
        var used = 0;
        var full = false;
        // Room is kept for naming every class after the one that does not fit.
        const int stub = 200;
        var remaining = classes.Count;
        foreach (var views in classes)
        {
            remaining--;
            if (full)
            {
                answer.Add(new ClassViews(views.ClassId, Truncated:
                    $"Not read: the answer had reached its size limit ({budget} characters). Ask for this class in another call."));
                continue;
            }

            var size = Size(views);
            if (used + size <= budget)
            {
                answer.Add(views);
                used += size;
                continue;
            }

            full = true;
            if (CutSource(views, budget - used - remaining * stub, sourceFromLine, Size) is { } cut)
            {
                answer.Add(cut);
                used += Size(cut);
                continue;
            }
            answer.Add(new ClassViews(views.ClassId, Truncated:
                $"Not read: with it the answer would have been over its size limit ({budget} characters). Ask for this " +
                "class in another call, and for a long source, its other views apart from it."));
        }
        return answer;
    }

    // The class with as many of its source lines as fit in room, and a note saying where to go on; null
    // when it has no source to cut or not even its first lines fit.
    private static ClassViews? CutSource(ClassViews views, int room, int fromLine, Func<ClassViews, int> size)
    {
        if (views.Source is not ClassSourceResult source)
            return null;
        var lines = source.Source.Split('\n');
        var first = Math.Max(fromLine, 1);

        ClassViews With(int count) => views with
        {
            Source = source with { Source = string.Join("\n", lines.Take(count)) },
            // sourceFromLine applies to every class a call names, so the class is asked for alone.
            Truncated = $"Source cut for size after line {first + count - 1}, of {first + lines.Length - 1}: ask for this " +
                        $"class alone, with views ['source'] and sourceFromLine={first + count}.",
        };

        // The most lines that fit, found by halving.
        int low = 0, high = lines.Length - 1;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (size(With(mid)) <= room)
                low = mid;
            else
                high = mid - 1;
        }
        return low >= Math.Max(1, Math.Min(10, lines.Length - 1)) ? With(low) : null;
    }

    [McpServerTool(Name = "mlqt_refactor_class", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Rename, move or delete a Modelica class, or rename one of its components. action 'rename' takes " +
                 "newName (the new short name) and 'move' takes newParentId (the package it goes into); both rewrite " +
                 "every reference to it across the loaded libraries, and refuse a change that would make a reference " +
                 "mean something else. 'rename_component' takes component (a parameter, constant, variable or " +
                 "connector declared in the class) and newName, and rewrites its declaration and every place that " +
                 "names it - references here, in derived classes and through components, modifications, named " +
                 "arguments - refusing a name already declared there. 'delete' removes the class and its storage, " +
                 "and lists the classes still referring to it once dependencies are analysed - those references are " +
                 "not rewritten. Every file is parse-checked before it is written; preview=true returns what would " +
                 "change without writing.")]
    public async Task<object> RefactorClass(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("'rename', 'move', 'delete' or 'rename_component'.")] string action,
        [Description("For 'rename' and 'rename_component': the new short name, not a full id.")] string? newName = null,
        [Description("For 'move': the full id of the package to move it into.")] string? newParentId = null,
        [Description("For 'rename_component': the component's current name, as declared in the class.")] string? component = null,
        [Description("Return what would change without writing. Default false.")] bool preview = false)
    {
        // First, as every editing tool does: a read-only class is refused whatever was asked of it.
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "rename, move or delete this class") is { } readOnlyClass)
            return readOnlyClass;

        // Each finds the classes to rewrite, or to report, from the dependency graph; the consolidated set
        // has no tool to build it, so this does (B503).
        if (action?.Trim().ToLowerInvariant() is "rename" or "move" or "delete" or "rename_component" && await AnalyseFirstAsync() is { } notAnalysed)
            return notAnalysed;

        switch (action?.Trim().ToLowerInvariant())
        {
            case "rename":
                return string.IsNullOrWhiteSpace(newName)
                    ? new ToolError("'rename' needs newName: the class's new short name.")
                    : await _edit.RenameClass(classId, newName, preview);
            case "rename_component":
                return string.IsNullOrWhiteSpace(component) ? new ToolError("'rename_component' needs component: the name it has now.")
                    : string.IsNullOrWhiteSpace(newName) ? new ToolError("'rename_component' needs newName: the name to give it.")
                    : await _edit.RenameComponent(classId, component, newName, preview);
            case "move":
                return string.IsNullOrWhiteSpace(newParentId)
                    ? new ToolError("'move' needs newParentId: the full id of the package to move the class into.")
                    : await _edit.MoveClass(classId, newParentId, preview);
            case "delete":
                return await _edit.DeleteClass(classId, preview);
            default:
                return new ToolError($"action must be 'rename', 'move', 'delete' or 'rename_component', not '{action}'.");
        }
    }

    /// <summary>Runs the dependency and resource analysis when the session has not, as
    /// <c>mlqt_dependencies</c> would; null when it has run, else its error.</summary>
    private async Task<ToolError?> AnalyseFirstAsync()
    {
        if (_session.DependenciesAnalyzed)
            return null;
        return await _dependencies.AnalyzeDependencies() as ToolError;
    }
}
