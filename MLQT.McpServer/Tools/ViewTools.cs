using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.DataTypes;
using ModelicaParser.SpellChecking;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// "Views" over a class — token-efficient projections so an agent can learn how to USE a class, or
/// what it CONTAINS, without reading its full source. get_class_interface (public API), list_class_elements
/// (every declaration), get_class_documentation (prose), and validate_class_references (undefined names).
/// All are read-only and need only a loaded library (not analyze_dependencies).
/// </summary>
[McpServerToolType]
public sealed class ViewTools
{
    private readonly ILibraryDataService _libraries;

    public ViewTools(ILibraryDataService libraries) => _libraries = libraries;

    [McpServerTool(Name = "get_class_interface")]
    [Description("Get the public interface of a class — how to USE it without reading its source: its " +
                "settable parameters (name/type/default/description), its connectors (with causality " +
                "input/output and flow/stream), its extends (base classes), and, for a function, its " +
                "input/output signature. Far smaller than get_class_source. A component is reported as a " +
                "connector when it has a causality or its type resolves to a loaded connector class; type " +
                "resolution is best-effort (see validate_class_references). Needs only a loaded library.")]
    public object GetClassInterface(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId)
    {
        if (Load(classId, out var node, out var iface, out var error))
            return error!;

        var imports = ImportNames(iface!);
        var isFunction = node!.ClassType == "function";

        var extends = iface!.Elements.Where(e => e.Kind == ClassElementKind.Extends).Select(e => e.Name).ToList();
        var parameters = new List<ParameterView>();
        var connectors = new List<ConnectorView>();
        var members = new List<MemberView>();

        foreach (var e in iface.Elements.Where(e => e.Kind == ClassElementKind.Component && e.IsPublic))
        {
            // A function's causal components are its signature, reported separately below.
            if (isFunction && e.Causality is not null)
                continue;

            var typeNode = TypeResolver.Resolve(_libraries, node.Id, e.Type, imports);
            var typeIsConnector = typeNode?.ClassType == "connector";
            var isConnector = !isFunction && (e.Causality is not null || typeIsConnector);

            if (isConnector)
                connectors.Add(new ConnectorView(e.Name, e.Type, e.Causality, e.Connection, typeIsConnector, e.Description));
            else if (e.Variability is "parameter" or "constant")
                parameters.Add(new ParameterView(e.Name, e.Type, e.Variability, e.DefaultValue, e.Description));
            else
                members.Add(new MemberView(e.Name, e.Type, e.Description));
        }

        FunctionSignatureView? signature = null;
        if (isFunction)
        {
            var comps = iface.Elements.Where(e => e.Kind == ClassElementKind.Component).ToList();
            ParameterView ToArg(ClassElement e) => new(e.Name, e.Type, e.Variability, e.DefaultValue, e.Description);
            signature = new FunctionSignatureView(
                comps.Where(e => e.Causality == "input").Select(ToArg).ToList(),
                comps.Where(e => e.Causality == "output").Select(ToArg).ToList());
        }

        return new ClassInterfaceView(
            node.Id, node.Name, node.ClassType, node.IsPartial, iface.Description,
            extends, parameters, connectors, members, signature);
    }

    [McpServerTool(Name = "list_class_elements")]
    [Description("List every declared element of a class in source order: components (with type, " +
                "variability parameter/constant/discrete, causality input/output, flow/stream, default " +
                "value, description), extends clauses, imports, and nested classes. By default only public " +
                "elements are returned; set include_protected=true to also include protected ones. This is " +
                "the granular data behind get_class_interface. Needs only a loaded library.")]
    public object ListClassElements(
        [Description("Fully-qualified class id.")] string classId,
        [Description("Include elements declared in protected sections. Default false.")]
        bool includeProtected = false)
    {
        if (Load(classId, out var node, out var iface, out var error))
            return error!;

        var elements = iface!.Elements
            .Where(e => includeProtected || e.IsPublic)
            .Select(e => new ClassElementView(
                e.Kind.ToString().ToLowerInvariant(),
                e.Name,
                e.Type,
                e.Variability,
                e.Causality,
                e.Connection,
                e.IsPublic ? "public" : "protected",
                e.DefaultValue,
                e.Description,
                e.ClassType,
                e.Prefixes,
                e.Line))
            .ToList();

        return new ClassElementsResult(node!.Id, elements.Count, elements);
    }

    [McpServerTool(Name = "get_class_documentation")]
    [Description("Get a class's documentation without its code: its description string plus the " +
                "Documentation(info=...) and Documentation(revisions=...) annotation text. format='text' " +
                "(default) strips HTML to plain text; format='html' returns the raw HTML. Use this to " +
                "understand what a class does. Needs only a loaded library.")]
    public object GetClassDocumentation(
        [Description("Fully-qualified class id.")] string classId,
        [Description("'text' (default, HTML stripped) or 'html' (raw).")] string format = "text")
    {
        if (Load(classId, out var node, out var iface, out var error))
            return error!;

        var (info, revisions) = DocumentationExtractor.Extract(node!.Definition.EnsureParsed());
        var asText = !string.Equals(format, "html", StringComparison.OrdinalIgnoreCase);
        if (asText)
        {
            info = info is null ? null : TextExtractor.StripHtml(info);
            revisions = revisions is null ? null : TextExtractor.StripHtml(revisions);
        }

        return new ClassDocumentationResult(node.Id, asText ? "text" : "html", iface!.Description, info, revisions);
    }

    [McpServerTool(Name = "validate_class_references")]
    [Description("Check that the types a class references (its component types and extends base classes) " +
                "resolve to loaded classes, and report those that do not — useful after writing or editing " +
                "a class to catch typos and missing dependencies. BEST-EFFORT: resolution uses exact, " +
                "import, and package-relative lookup but does NOT model names inherited via extends, so a " +
                "reported name may be a false positive (brought into scope by a base class) — treat the " +
                "list as candidates. Load the referenced libraries too so their classes can resolve.")]
    public object ValidateClassReferences(
        [Description("Fully-qualified class id to validate.")] string classId)
    {
        if (Load(classId, out var node, out var iface, out var error))
            return error!;

        var imports = ImportNames(iface!);
        var checkedCount = 0;
        var unresolved = new List<UnresolvedReference>();

        foreach (var e in iface!.Elements)
        {
            string? type;
            string kind;
            if (e.Kind == ClassElementKind.Component)
            {
                type = e.Type;
                kind = "component-type";
            }
            else if (e.Kind == ClassElementKind.Extends)
            {
                type = e.Type;
                kind = "extends";
            }
            else
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(type) || TypeResolver.IsPredefined(type))
                continue;

            checkedCount++;
            if (TypeResolver.Resolve(_libraries, node!.Id, type, imports) is null)
                unresolved.Add(new UnresolvedReference(type!.TrimStart('.').Trim(), kind, e.Line));
        }

        const string note = "Best-effort: resolution does not model names inherited via extends or every " +
                            "import form, so a listed reference may be a false positive. Ensure the " +
                            "referenced libraries are loaded before treating a name as genuinely undefined.";
        return new ReferenceValidationResult(node!.Id, checkedCount, unresolved.Count, unresolved, note);
    }

    // Resolves the class, guards parse health, and extracts its interface. Returns true (with a populated
    // 'error') when the caller should return early.
    private bool Load(string classId, out ModelicaGraph.DataTypes.ModelNode? node, out ClassInterface? iface, out object? error)
    {
        iface = null;
        error = null;
        node = _libraries.GetModelById(classId);
        if (node is null)
        {
            error = ToolDiagnostics.ClassNotFound(_libraries, classId);
            return true;
        }
        if (node.IsParseFailurePlaceholder)
        {
            error = new ToolError($"Class '{classId}' failed to parse; its structure cannot be read.");
            return true;
        }

        var tree = node.Definition.EnsureParsed();
        if (tree is null)
        {
            error = new ToolError($"Class '{classId}' has no readable source.");
            return true;
        }

        iface = ClassInterfaceExtractor.Extract(tree);
        return false;
    }

    private static IReadOnlyList<string> ImportNames(ClassInterface iface)
        => iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList();
}
