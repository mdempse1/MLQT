using ModelicaGraph.Analysis;
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
                "input/output signature. Members INHERITED via extends are included by default (each marked " +
                "with the base class it came from in inheritedFrom), so you get the complete picture without " +
                "chasing base classes — set include_inherited=false for only what the class declares itself. " +
                "Far smaller than get_class_source. A component is a connector when it has a causality or its " +
                "type resolves to a loaded connector class. A parameter's default is the value it takes; a " +
                "typeModification (e.g. \"(min=0)\") constrains its type and is reported apart from the " +
                "default, since a declaration can carry both. Needs only a loaded library.")]
    public object GetClassInterface(
        [Description("Fully-qualified class id, e.g. 'Modelica.Blocks.Continuous.Integrator'.")]
        string classId,
        [Description("Include members inherited from base classes (default true).")]
        bool includeInherited = true)
    {
        if (Load(classId, out var node, out var iface, out var error))
            return error!;

        var isFunction = node!.ClassType == "function";
        var merged = ClassElementResolver.Collect(_libraries.CombinedGraph, node, includeProtected: false, includeInherited);

        var extends = merged.Where(m => m.Element.Kind == ClassElementKind.Extends)
            .Select(m => m.Element.Name).ToList();
        var parameters = new List<ParameterView>();
        var connectors = new List<ConnectorView>();
        var members = new List<MemberView>();

        foreach (var m in merged.Where(m => m.Element.Kind == ClassElementKind.Component))
        {
            var e = m.Element;
            // A function's causal components are its signature, reported separately below.
            if (isFunction && e.Causality is not null)
                continue;

            // Resolve the type in the scope of the class that DECLARED the component (base or self).
            var typeNode = TypeResolver.Resolve(_libraries.CombinedGraph, m.OwnerId, e.Type, m.OwnerImports);
            var typeIsConnector = typeNode?.ClassType == "connector";
            var isConnector = !isFunction && (e.Causality is not null || typeIsConnector);

            if (isConnector)
                connectors.Add(new ConnectorView(e.Name, e.Type, e.Causality, e.Connection, typeIsConnector, e.Description, m.InheritedFrom));
            else if (e.Variability is "parameter" or "constant")
                parameters.Add(new ParameterView(
                    e.Name, e.Type, e.Variability, e.DefaultValue, e.TypeModification,
                    e.Description, m.InheritedFrom));
            else
                members.Add(new MemberView(e.Name, e.Type, e.Description, m.InheritedFrom));
        }

        FunctionSignatureView? signature = null;
        if (isFunction)
        {
            ParameterView ToArg(ResolvedElement m) =>
                new(m.Element.Name, m.Element.Type, m.Element.Variability, m.Element.DefaultValue,
                    m.Element.TypeModification, m.Element.Description, m.InheritedFrom);
            var comps = merged.Where(m => m.Element.Kind == ClassElementKind.Component).ToList();
            signature = new FunctionSignatureView(
                comps.Where(m => m.Element.Causality == "input").Select(ToArg).ToList(),
                comps.Where(m => m.Element.Causality == "output").Select(ToArg).ToList());
        }

        return new ClassInterfaceView(
            node.Id, node.Name, node.ClassType, node.IsPartial, iface!.Description,
            extends, parameters, connectors, members, signature);
    }

    [McpServerTool(Name = "list_class_elements")]
    [Description("List the elements of a class: components (with type, variability parameter/constant/" +
                "discrete, causality input/output, flow/stream, default value, description), extends " +
                "clauses, imports, and nested classes. Members INHERITED via extends are included by " +
                "default (each marked with its base class in inheritedFrom); set include_inherited=false " +
                "for only the class's own declarations. By default only public elements are returned; set " +
                "include_protected=true to also include protected ones. Each element also carries any " +
                "leadingComments (the // or /* */ comments written just above it). This is the granular " +
                "data behind get_class_interface. A component's default is the value it is bound to; its " +
                "typeModification is any modification written on its type (e.g. \"(min=0)\" or \"(k=2)\"), " +
                "which is not a value. Needs only a loaded library.")]
    public object ListClassElements(
        [Description("Fully-qualified class id.")] string classId,
        [Description("Include elements declared in protected sections. Default false.")]
        bool includeProtected = false,
        [Description("Include elements inherited from base classes. Default true.")]
        bool includeInherited = true)
    {
        if (Load(classId, out var node, out _, out var error))
            return error!;

        var elements = ClassElementResolver.Collect(_libraries.CombinedGraph, node!, includeProtected, includeInherited)
            .Select(m => new ClassElementView(
                m.Element.Kind.ToString().ToLowerInvariant(),
                m.Element.Name,
                m.Element.Type,
                m.Element.Variability,
                m.Element.Causality,
                m.Element.Connection,
                m.Element.IsPublic ? "public" : "protected",
                m.Element.DefaultValue,
                m.Element.TypeModification,
                m.Element.Description,
                m.Element.ClassType,
                m.Element.Prefixes,
                m.Element.LeadingComments,
                m.Element.Line,
                m.InheritedFrom))
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

    [McpServerTool(Name = "get_class_behavior")]
    [Description("Get the behavior a class declares itself: its top-level equations, connect() statements " +
                "and algorithm statements (each equation/statement carries any leadingComments written " +
                "above it). Unlike the interface views, inherited behavior is NOT merged in (equations " +
                "reference their own class's scope) — instead basesWithBehavior lists the base classes that " +
                "declare behavior, which you can query directly for the full picture. Read-only.")]
    public object GetClassBehavior(
        [Description("Fully-qualified class id.")] string classId)
    {
        if (Load(classId, out var node, out _, out var error))
            return error!;

        var behavior = BehaviorExtractor.ExtractFromCode(node!.Definition.ModelicaCode ?? string.Empty);
        var connections = behavior.Connections.Select(c => new ConnectionView(c.PortA, c.PortB)).ToList();
        BehaviorLineView ToLine(ModelicaParser.DataTypes.BehaviorLine l) => new(l.Text, l.LeadingComments);
        return new ClassBehaviorResult(
            classId,
            behavior.Equations.Select(ToLine).ToList(),
            connections,
            behavior.Statements.Select(ToLine).ToList(),
            CollectBasesWithBehavior(classId));
    }

    // Ancestors (via extends) whose own bodies declare equations, connections or statements.
    private List<string> CollectBasesWithBehavior(string classId)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { classId };

        void Walk(string id, int depth)
        {
            if (depth > 32)
                return;
            var tree = _libraries.GetModelById(id)?.Definition.EnsureParsed();
            if (tree is null)
                return;
            var iface = ClassInterfaceExtractor.Extract(tree);
            var imports = iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList();
            foreach (var ext in iface.Elements.Where(e => e.Kind == ClassElementKind.Extends))
            {
                var baseNode = TypeResolver.Resolve(_libraries.CombinedGraph, id, ext.Type, imports);
                if (baseNode is null || !visited.Add(baseNode.Id))
                    continue;
                if (BehaviorExtractor.ExtractFromCode(baseNode.Definition.ModelicaCode ?? string.Empty).HasAny)
                    result.Add(baseNode.Id);
                Walk(baseNode.Id, depth + 1);
            }
        }

        Walk(classId, 0);
        return result;
    }

    [McpServerTool(Name = "validate_class_references")]
    [Description("Check that the types a class references (its component types and extends base classes) " +
                "resolve to loaded classes, and report those that do not — useful after writing or editing " +
                "a class to catch typos and missing dependencies. Resolution uses exact, import and " +
                "package-relative lookup AND names inherited via extends (an inherited type is not flagged). " +
                "Still best-effort for uncommon import forms, so load the referenced libraries so their " +
                "classes can resolve before treating a name as genuinely undefined.")]
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
            if (TypeResolver.ResolveWithInheritance(_libraries.CombinedGraph, node!.Id, type, imports) is null)
                unresolved.Add(new UnresolvedReference(type!.TrimStart('.').Trim(), kind, e.Line));
        }

        const string note = "Resolution covers the class's own scope plus names inherited via extends. It " +
                            "is still best-effort for uncommon import forms, so ensure the referenced " +
                            "libraries are loaded before treating a name as genuinely undefined.";
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
