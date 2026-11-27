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
/// what it CONTAINS, without reading its full source. mlqt_get_class's interface view (public API), mlqt_get_class's elements view
/// (every declaration), mlqt_get_class's documentation view (prose), and mlqt_check's references check (undefined names).
/// All are read-only and need only a loaded library (not mlqt_dependencies).
/// </summary>
[Obfuscation]
public sealed class ViewTools
{
    private readonly ILibraryDataService _libraries;

    public ViewTools(ILibraryDataService libraries) => _libraries = libraries;

    internal object GetClassInterface(
        string classId,
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

    internal object ListClassElements(
        string classId,
        bool includeProtected = false,
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

    /// <summary>
    /// The most characters of base documentation one documentation view holds. A class with a long
    /// chain - an interface, a library's own interface on it, a template, an example - would otherwise
    /// carry every one in full, and mlqt_get_class answers up to 20 classes in one call; the bases past
    /// the cap are named, so an agent can read the one it wants.
    /// </summary>
    internal const int MaxBaseDocumentationCharacters = 12_000;

    /// <param name="includeBases">Also each base's documentation, nearest first: what a class does is
    /// often written once, on the interface it extends, and an agent read class after class up the chain
    /// to find it (2026-11-27).</param>
    internal object GetClassDocumentation(
        string classId,
        string format = "text",
        bool includeBases = false)
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

        var result = new ClassDocumentationResult(node.Id, asText ? "text" : "html", iface!.Description, info, revisions);
        return includeBases ? WithBases(result, node, asText) : result;
    }

    // What one base is, read once: its prose, and whether it declares or does anything itself.
    private sealed record BaseFacts(string? Description, string? Info, bool HasContent);

    // The class's bases' documentation, nearest first, up to the cap; the rest named, the skipped
    // counted, the unloaded named.
    private ClassDocumentationResult WithBases(ClassDocumentationResult result, ModelNode node, bool asText)
    {
        var graph = _libraries.CombinedGraph;
        var (chain, unloaded) = BasesNearestFirst(graph, node);
        var facts = new Dictionary<string, BaseFacts>(StringComparer.Ordinal);
        var iconOnly = new Dictionary<string, bool>(StringComparer.Ordinal);

        var bases = new List<BaseDocumentation>();
        var more = new List<string>();
        var skipped = 0;
        var used = 0;
        foreach (var baseNode in chain)
        {
            var (description, info, _) = FactsOf(baseNode, facts);
            if (asText && info is not null)
                info = TextExtractor.StripHtml(info);

            // A base with no prose, or one that only draws - an icon class, which declares nothing and
            // extends only other icon classes - says nothing about what the class does. MSL documents its
            // icons ("This icon indicates Modelica functions."), and skipping only the undocumented ones
            // quoted Modelica.Icons.Function in 1,781 of its classes' answers.
            if (string.IsNullOrWhiteSpace(info) || IsIconOnly(graph, baseNode, facts, iconOnly))
            {
                skipped++;
                continue;
            }
            var size = info.Length + (description?.Length ?? 0);
            if (more.Count > 0 || (bases.Count > 0 && used + size > MaxBaseDocumentationCharacters))
            {
                more.Add(baseNode.Id);
                continue;
            }
            // The nearest base is given whole whatever its size, cut only where it alone is over the cap.
            if (size > MaxBaseDocumentationCharacters)
                info = CutAt(info, Math.Max(0, MaxBaseDocumentationCharacters - (description?.Length ?? 0)), asText) +
                       $" ... (cut at {MaxBaseDocumentationCharacters} characters; ask for {baseNode.Id}'s documentation view)";
            bases.Add(new BaseDocumentation(baseNode.Id, description, info));
            used += size;
        }

        return result with
        {
            Bases = bases.Count > 0 ? bases : null,
            MoreBases = more.Count > 0 ? more : null,
            SkippedBases = skipped > 0 ? skipped : null,
            UnloadedBases = unloaded.Count > 0 ? unloaded : null,
        };
    }

    // Read once per call: a diamond, or an icon class reached through several bases, is asked about again.
    private static BaseFacts FactsOf(ModelNode node, Dictionary<string, BaseFacts> facts)
    {
        if (facts.TryGetValue(node.Id, out var known))
            return known;
        // Borrowed: a base's tree is not this call's to keep.
        var read = node.Definition.Borrow(tree =>
        {
            var iface = ClassInterfaceExtractor.Extract(tree);
            var declares = iface.Elements.Any(e => e.Kind is ClassElementKind.Component or ClassElementKind.Class);
            return new BaseFacts(iface.Description, DocumentationExtractor.Extract(tree).Info,
                declares || BehaviorExtractor.ExtractFromCode(node.Definition.ModelicaCode ?? string.Empty).HasAny);
        }, new BaseFacts(null, null, HasContent: true));
        facts[node.Id] = read;
        return read;
    }

    /// <summary>
    /// Whether <paramref name="node"/> only draws: it declares no component or class, has no equation or
    /// algorithm, and every base it extends is the same. A template that extends an interface with nothing
    /// of its own is not one - what it extends declares something - and its documentation is kept.
    /// </summary>
    private static bool IsIconOnly(ModelicaGraph.DirectedGraph graph, ModelNode node,
        Dictionary<string, BaseFacts> facts, Dictionary<string, bool> memo, int depth = 0)
    {
        if (memo.TryGetValue(node.Id, out var known))
            return known;
        memo[node.Id] = false;   // a cycle is no icon
        var only = !FactsOf(node, facts).HasContent && depth < 32
                   && ModelicaGraph.ClassImports.BasesOf(node.Definition).Count(b => !TypeResolver.IsPredefined(b))
                       == ClassElementResolver.DirectBases(graph, node).Count
                   && ClassElementResolver.DirectBases(graph, node).All(b => IsIconOnly(graph, b.Base, facts, memo, depth + 1));
        memo[node.Id] = only;
        return only;
    }

    // At most `length` characters of `text`; for HTML, never inside a tag, which would leave the markup
    // broken for whatever renders it.
    private static string CutAt(string text, int length, bool asText)
    {
        var cut = text[..Math.Min(length, text.Length)];
        if (asText)
            return cut;
        var open = cut.LastIndexOf('<');
        return open > cut.LastIndexOf('>') ? cut[..open] : cut;
    }

    /// <summary>
    /// Every class <paramref name="node"/> extends, transitively, nearest first: its own extends clauses
    /// in order, then theirs, a base reached twice listed once. A short class's base is its base.
    /// <see cref="ClassElementResolver.BaseClasses"/> gives them deepest first, the order a diagram
    /// draws them in; documentation is read the other way, from the class outward. <c>Unloaded</c> is
    /// each base written in an extends clause that does not resolve - its library not loaded, or a name
    /// that is wrong - as "Name, extended by Class", since without it the chain read as complete.
    /// </summary>
    internal static (List<ModelNode> Bases, List<string> Unloaded) BasesNearestFirst(
        ModelicaGraph.DirectedGraph graph, ModelNode node)
    {
        var result = new List<ModelNode>();
        var unloaded = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { node.Id };
        var level = new List<ModelNode> { node };
        for (var depth = 0; level.Count > 0 && depth < 32; depth++)
        {
            var next = new List<ModelNode>();
            foreach (var cls in level)
            {
                var resolved = ClassElementResolver.DirectBases(graph, cls);
                foreach (var written in ModelicaGraph.ClassImports.BasesOf(cls.Definition))
                {
                    // A predefined type (type Length = Real(...)) is the language's, not a library's, and
                    // has no class to resolve to.
                    if (!resolved.Any(r => r.Written == written) && !TypeResolver.IsPredefined(written))
                        unloaded.Add($"{written}, extended by {cls.Id}");
                }
                foreach (var (_, baseNode) in resolved)
                {
                    if (visited.Add(baseNode.Id))
                        next.Add(baseNode);
                }
            }
            result.AddRange(next);
            level = next;
        }
        return (result, unloaded);
    }

    internal object GetClassBehavior(
        string classId)
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

    /// <summary>The id of a references check given no target, which covers every library loaded.</summary>
    internal const string EveryLibraryLoaded = "(every library loaded)";

    /// <summary>The largest page mlqt_check's references check returns; a bigger limit is clamped, not refused.</summary>
    internal const int MaxReferenceLimit = 1000;

    internal object ValidateClassReferences(
        string? classId = null,
        string? libraryId = null,
        int limit = 100,
        int offset = 0)
    {
        // The names are mlqt_check's parameters. This said class_id and library_id, which the tool refused,
        // and with neither it refused too, where mlqt_check promises the libraries loaded, as style and
        // spelling give them (2026-11-27).
        if (classId is not null && libraryId is not null)
            return new ToolError("Give classId or libraryId, not both: classId checks a class and everything " +
                                 "inside it, libraryId a whole library.");

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
            if (libraryId is null)
            {
                // Every library loaded, as mlqt_check's style and spelling checks take no target. The id
                // says so rather than listing them: mlqt_session lists what is loaded.
                scopeId = EveryLibraryLoaded;
                scope = _libraries.Libraries.SelectMany(l => l.ModelIds).Select(_libraries.GetModelById).OfType<ModelNode>();
            }
            else
            {
                var (library, error) = EntityResolver.ResolveLibrary(_libraries, libraryId);
                if (error is not null)
                    return error;
                scopeId = library!.Name;
                scope = library.ModelIds.Select(_libraries.GetModelById).OfType<ModelNode>();
            }
        }

        var graph = _libraries.CombinedGraph;
        var classesChecked = 0;
        var checkedCount = 0;
        var unreadable = new List<string>();
        var unresolved = new List<UnresolvedReference>();
        var recovered = 0;

        foreach (var node in scope.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            // A class rebuilt from an encrypted library's help pages is a declaration MLQT wrote, not the
            // vendor's code, and nobody can fix what it names; the per-class runner every other check goes
            // through skips it too. With no target the check covers every library loaded, encrypted
            // ones included, so reading them would report the session's vendor builds by default.
            if (node.IsExternalStub)
            {
                recovered++;
                continue;
            }
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

        var note = "Resolution covers each class's own scope plus names inherited via extends. It " +
                   "is still best-effort for uncommon import forms, so ensure the referenced " +
                   "libraries are loaded before treating a name as genuinely undefined." +
                   (recovered > 0
                       ? $" Not checked, as their source is not readable: {recovered} class{(recovered == 1 ? "" : "es")} " +
                         "recovered from encrypted libraries."
                       : string.Empty);
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
