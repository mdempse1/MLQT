using System.Reflection;
using ModelicaGraph.Analysis;
using ModelicaParser.Helpers;
using MLQT.Services.Checking;
using ModelicaGraph.DataTypes;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.DataTypes;
using ModelicaParser.ExternalDocs;
using ModelicaParser.SpellChecking;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// "Views" over a class — token-efficient projections so an agent can learn how to USE a class, or
/// what it CONTAINS, without reading its full source. mlqt_get_class_interface (public API), mlqt_list_class_elements
/// (every declaration), mlqt_get_class_documentation (prose), and mlqt_validate_class_references (undefined names).
/// All are read-only and need only a loaded library (not mlqt_analyze_dependencies).
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class ViewTools
{
    private readonly ILibraryDataService _libraries;

    public ViewTools(ILibraryDataService libraries) => _libraries = libraries;

    [McpServerTool(Name = "mlqt_get_class_interface")]
    [Description("Get the public interface of a Modelica class — how to USE it without reading its source: its " +
                "settable parameters (name/type/default/description), its connectors (with causality " +
                "input/output and flow/stream), its extends (base classes), and, for a function, its " +
                "input/output signature. Members INHERITED via extends are included by default (each marked " +
                "with the base class it came from in inheritedFrom), so you get the complete picture without " +
                "chasing base classes — set include_inherited=false for only what the class declares itself. " +
                "Far smaller than mlqt_get_class_source. A component is a connector when it has a causality or its " +
                "type resolves to a loaded connector class. A connector carrying a 'condition' is " +
                "CONDITIONAL - it exists only where that expression is true, so an instance that leaves it " +
                "false has no such port and connecting to it is an error. A parameter's default is the value it takes; a " +
                "typeModification (e.g. \"(min = 0)\") constrains its type and is reported apart from the " +
                "default, since a declaration can carry both. A default set by a modification rather than by the declaration - an extends clause in a derived class, or a short class - names the class that wrote it in modifiedIn: the value is an expression in that class, so its names resolve there and not in inheritedFrom. For a class from an encrypted library, " +
                "recoveredFromDocumentation is true and the members come from the vendor's generated " +
                "help: names, descriptions and units, with no types, defaults or inheritance - that is " +
                "everything the vendor published. Needs only a loaded library.")]
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

        // A class from an encrypted library: its extends are real declarations in the synthesized
        // source and are collected above, but its members were never expressible as Modelica and
        // come off the documentation instead (B179). includeInherited has no meaning here - the
        // vendor's tables already list what a class inherits.
        if (RecoveredInterface.For(node) is { } documented)
            return RecoveredInterface.ToInterfaceView(node, documented, extends);
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
                connectors.Add(new ConnectorView(
                    e.Name, e.Type, e.Causality, e.Connection, typeIsConnector, e.Description,
                    m.InheritedFrom, Unit: null, e.Condition));
            else if (e.Variability is "parameter" or "constant")
                parameters.Add(new ParameterView(
                    e.Name, e.Type, e.Variability, e.DefaultValue, e.TypeModification,
                    e.Description, m.InheritedFrom, ModifiedIn: m.ModifiedIn));
            else
                members.Add(new MemberView(e.Name, e.Type, e.Description, m.InheritedFrom));
        }

        FunctionSignatureView? signature = null;
        if (isFunction)
        {
            ParameterView ToArg(ResolvedElement m) =>
                new(m.Element.Name, m.Element.Type, m.Element.Variability, m.Element.DefaultValue,
                    m.Element.TypeModification, m.Element.Description, m.InheritedFrom, ModifiedIn: m.ModifiedIn);
            var comps = merged.Where(m => m.Element.Kind == ClassElementKind.Component).ToList();
            signature = new FunctionSignatureView(
                comps.Where(m => m.Element.Causality == "input").Select(ToArg).ToList(),
                comps.Where(m => m.Element.Causality == "output").Select(ToArg).ToList());
        }

        return new ClassInterfaceView(
            node.Id, node.Name, node.ClassType, node.IsPartial, iface!.Description,
            extends, parameters, connectors, members, signature);
    }

    [McpServerTool(Name = "mlqt_list_class_elements")]
    [Description("List the elements of a Modelica class: components (with type, variability parameter/constant/" +
                "discrete, causality input/output, flow/stream, default value, description), extends " +
                "clauses, imports, and nested classes. Members INHERITED via extends are included by " +
                "default (each marked with its base class in inheritedFrom); set include_inherited=false " +
                "for only the class's own declarations. By default only public elements are returned; set " +
                "include_protected=true to also include protected ones. Each element also carries any " +
                "leadingComments (the // or /* */ comments written just above it). This is the granular " +
                "data behind mlqt_get_class_interface. A component's default is the value it is bound to; its " +
                "typeModification is any modification written on its type, as written (e.g. \"(min = 0)\" or \"(k = 2)\"), " +
                "which is not a value. arraySubscripts are its dimensions as written, the declaration's before the type's " +
                "(Real[2] x[3] gives \"[3, 2]\"). redeclarations lists each redeclare (or replaceable) its modification makes, " +
                "keyed by the path to the element replaced (\"Medium\", or \"b.Medium\" for b(redeclare package Medium = W)), " +
                "with the replacing type - so Medium.T in that instance means the redeclared medium's T. A nested short class " +
                "(type Torque = Real(unit = \"N.m\")) carries the base it names in type and what it applies in typeModification. A default set by a modification rather than by the declaration - an extends clause in a derived class, or a short class - names the class that wrote it in modifiedIn: the value is an expression in that class, so its names resolve there and not in inheritedFrom. For a class from an encrypted library, recoveredFromDocumentation " +
                "is true and its members come from the vendor's generated help - name, description, " +
                "unit and which table they were in (parameter / connector / input / output), with no " +
                "type, no default and line 0; one it inherits from a readable base is listed once, marked " +
                "with that base in inheritedFrom. Needs only a loaded library.")]
    public object ListClassElements(
        [Description("Fully-qualified class id.")] string classId,
        [Description("Include elements declared in protected sections. Default false.")]
        bool includeProtected = false,
        [Description("Include elements inherited from base classes. Default true.")]
        bool includeInherited = true)
    {
        if (Load(classId, out var node, out _, out var error))
            return error!;

        var recovered = RecoveredInterface.For(node);
        if (recovered is not null)
            return RecoveredElements(node!, recovered, includeProtected, includeInherited);

        var elements = ClassElementResolver.Collect(_libraries.CombinedGraph, node!, includeProtected, includeInherited)
            .Select(ToElementView)
            .ToList();

        return new ClassElementsResult(node!.Id, elements.Count, elements, RecoveredFromDocumentation: false);
    }

    /// <summary>
    /// The elements of an encrypted-library stub: its documented members, merged by name with
    /// whatever the resolver finds by following the stub's synthesized <c>extends</c> into a
    /// readable base (B318).
    ///
    /// <para>The vendor's generated help lists what a class inherits, so a block built on MSL's
    /// <c>SISO</c> documents <c>u</c> and <c>y</c> - which the resolver also finds in <c>SISO</c>.
    /// Appending one list to the other listed both twice. The documented member is kept, because it
    /// carries the role the table gave it; the resolver contributes where it came from, which is what
    /// lets <c>include_inherited=false</c> leave it out, and any readable member the documentation
    /// does not mention.</para>
    /// </summary>
    private ClassElementsResult RecoveredElements(
        ModelNode node, DocumentedClass recovered, bool includeProtected, bool includeInherited)
    {
        var resolved = ClassElementResolver.Collect(
            _libraries.CombinedGraph, node, includeProtected, includeInherited: true);

        var inheritedFrom = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in resolved.Where(m => m.InheritedFrom is not null))
            inheritedFrom.TryAdd(m.Element.Name, m.InheritedFrom!);

        var documented = RecoveredInterface.ToElementViews(recovered)
            .Select(v => inheritedFrom.TryGetValue(v.Name, out var from) ? v with { InheritedFrom = from } : v)
            .Where(v => includeInherited || v.InheritedFrom is null)
            .ToList();
        var documentedNames = RecoveredInterface.ToElementViews(recovered)
            .Select(v => v.Name)
            .ToHashSet(StringComparer.Ordinal);

        var elements = resolved
            .Where(m => includeInherited || m.InheritedFrom is null)
            .Where(m => m.Element.Kind is ClassElementKind.Extends or ClassElementKind.Import
                        || !documentedNames.Contains(m.Element.Name))
            .Select(ToElementView)
            .Concat(documented)
            .ToList();

        return new ClassElementsResult(node.Id, elements.Count, elements, RecoveredFromDocumentation: true);
    }

    private static ClassElementView ToElementView(ResolvedElement m) => new(
        WireNames.Of(m.Element.Kind).ToLowerInvariant(),
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
        m.Element.Condition,
        m.InheritedFrom,
        m.ModifiedIn,
        m.Element.ArraySubscripts,
        m.Element.Redeclarations?.ToDictionary(
            r => r.Key,
            r => new RedeclarationView(r.Value.ClassType, r.Value.Type, r.Value.TypeModification, r.Value.ArraySubscripts)));

    [McpServerTool(Name = "mlqt_get_class_documentation")]
    [Description("Get a Modelica class's documentation without its code: its description string plus the " +
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

    [McpServerTool(Name = "mlqt_get_class_behavior")]
    [Description("Get the behavior a Modelica class declares itself: its top-level equations, connect() statements " +
                "and algorithm statements (each equation/statement carries any leadingComments written " +
                "above it). connections includes those inside a for/if/when equation, each with 'within' " +
                "naming the branches it is in (e.g. ['for i in 1:n']) - such an equation is also listed, " +
                "verbatim, in equations. Unlike the interface views, inherited behavior is NOT merged in (equations " +
                "reference their own class's scope) — instead basesWithBehavior lists the base classes that " +
                "declare behavior, which you can query directly for the full picture. Read-only.")]
    public object GetClassBehavior(
        [Description("Fully-qualified class id.")] string classId)
    {
        if (Load(classId, out var node, out _, out var error))
            return error!;

        var behavior = BehaviorExtractor.ExtractFromCode(node!.Definition.ModelicaCode ?? string.Empty);
        var connections = behavior.Connections.Select(ConnectionView.Of).ToList();
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

    /// <summary>The largest page mlqt_validate_class_references returns; a bigger limit is clamped, not refused.</summary>
    internal const int MaxReferenceLimit = 1000;

    [McpServerTool(Name = "mlqt_validate_class_references")]
    [Description("Check that the types Modelica classes reference (their component types and extends base classes) " +
                "resolve to loaded classes, and report those that do not - the way to find a misspelt or missing " +
                "type name, after writing or editing code or when looking for a broken reference. Give class_id " +
                "for one class (a package is checked with every class inside it), or library_id for a whole " +
                "library: ONE call covers every class in it, so there is no need to call it class by class. Each " +
                "unresolved reference names its class, its line in that class's source, and its file and line on " +
                "disk; they are ordered by class and paged with limit/offset, and unresolvedCount is the total. " +
                "Resolution uses exact, import and package-relative lookup AND names inherited via extends (an " +
                "inherited type is not flagged). Load the libraries the code uses (e.g. the MSL) first, or every " +
                "reference into them is reported as unresolved. This is not what mlqt_check_library's " +
                "validateModelReferences setting does - that checks modelica:// links in documentation.")]
    public object ValidateClassReferences(
        [Description("A class id, e.g. 'MyLib.Pumps.Pump'. A package, or any class with classes nested in it, " +
                     "is checked together with everything inside it. Give this or library_id, not both.")]
        string? classId = null,
        [Description("A whole library to check, by its id (GUID from mlqt_list_libraries) or its name " +
                     "(e.g. 'MyLib'). Give this or class_id, not both.")]
        string? libraryId = null,
        [Description("Max unresolved references to return (default 100, max 1000).")] int limit = 100,
        [Description("Unresolved references to skip for pagination (default 0).")] int offset = 0)
    {
        if (classId is not null && libraryId is not null)
            return new ToolError("Give class_id or library_id, not both: class_id checks a class and everything " +
                                 "inside it, library_id a whole library.");
        if (classId is null && libraryId is null)
            return new ToolError("Give class_id (a class, or a package to check everything inside it) or " +
                                 "library_id (a whole library).");

        limit = Math.Clamp(limit, 1, MaxReferenceLimit);
        offset = Math.Max(offset, 0);

        string scopeId;
        IEnumerable<ModelNode> scope;
        if (classId is not null)
        {
            // The named class keeps the errors the one-class form always gave: not found, or unreadable.
            if (Load(classId, out var root, out _, out var error))
                return error!;
            scopeId = root!.Id;
            scope = _libraries.GetAllModels().Where(m => ModelicaName.IsInSubtree(m.Id, root.Id));
        }
        else
        {
            if (ToolDiagnostics.RequireLibrary(_libraries, "validating references") is { } noLib)
                return noLib;
            var (library, error) = EntityResolver.ResolveLibrary(_libraries, libraryId!);
            if (error is not null)
                return error;
            scopeId = library!.Name;
            scope = library.ModelIds.Select(_libraries.GetModelById).OfType<ModelNode>();
        }

        var graph = _libraries.CombinedGraph;
        var classesChecked = 0;
        var checkedCount = 0;
        var unreadable = new List<string>();
        var unresolved = new List<UnresolvedReference>();

        foreach (var node in scope.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            // Borrowed, not kept: a library's worth of parse trees held at once is what Borrow exists to
            // prevent, and the interface is all that is needed from each.
            var iface = node.IsParseFailurePlaceholder
                ? null
                : node.Definition.Borrow<ClassInterface?>(ClassInterfaceExtractor.Extract, ifUnparseable: null);
            if (iface is null)
            {
                unreadable.Add(node.Id);
                continue;
            }

            classesChecked++;
            var imports = ImportNames(iface);
            ClassLocation? location = null;
            foreach (var (type, kind, line) in ReferencedTypes(iface))
            {
                checkedCount++;
                if (TypeResolver.ResolveWithInheritance(graph, node.Id, type, imports) is not null)
                    continue;
                location ??= ClassFileLocation.Of(graph, node);
                unresolved.Add(new UnresolvedReference(
                    type.TrimStart('.').Trim(), kind, line, node.Id, location?.FilePath, location?.FileLine(line)));
            }
        }

        const string note = "Resolution covers each class's own scope plus names inherited via extends. It " +
                            "is still best-effort for uncommon import forms, so ensure the referenced " +
                            "libraries are loaded before treating a name as genuinely undefined.";
        var page = unresolved.Skip(offset).Take(limit).ToList();
        return new ReferenceValidationResult(
            scopeId, checkedCount, unresolved.Count, page, note,
            classesChecked, offset, page.Count, unreadable.Count == 0 ? null : unreadable);
    }

    /// <summary>The types a class names that must resolve: each component's type and each extends
    /// clause's base, less the predefined types.</summary>
    private static IEnumerable<(string Type, string Kind, int Line)> ReferencedTypes(ClassInterface iface)
    {
        foreach (var e in iface.Elements)
        {
            var kind = e.Kind switch
            {
                ClassElementKind.Component => "component-type",
                ClassElementKind.Extends => "extends",
                _ => null,
            };
            if (kind is null || string.IsNullOrWhiteSpace(e.Type) || TypeResolver.IsPredefined(e.Type))
                continue;
            yield return (e.Type, kind, e.Line);
        }
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
