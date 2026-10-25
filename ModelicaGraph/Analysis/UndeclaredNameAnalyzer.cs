using System.Collections.Concurrent;
using ModelicaGraph.DataTypes;
using ModelicaGraph.ParserVisitors;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using RuleIdsRef = ModelicaParser.StyleRules.RuleIds;

namespace ModelicaGraph.Analysis;

/// <summary>
/// Flags a name that is used where nothing of that name is declared: a variable in an equation, a
/// binding or a subscript, a function called, a type, a base class, an import, a member named after a
/// dot, and the target of a modification.
/// </summary>
/// <remarks>
/// <para><b>Lookup is the resolver's</b> (<see cref="ComponentReferences"/>, <see cref="TypeResolver"/>):
/// the class's own and inherited elements, its imports, the constants and classes of its enclosing
/// classes up to an <c>encapsulated</c> one, and the top-level classes (MLS §5.3). A name it resolves
/// is declared. A name it does not is walked again, segment by segment, to find the first one that
/// fails and to decide whether that failure is known.</para>
///
/// <para><b>Three-state, as the roadmap's Wave 2 requires: a miss is reported only when it could not
/// have been a hit.</b> Every scope the lookup passed through has to be readable source with every
/// base resolved - an encrypted class, a base in a library that is not loaded, or an import of one,
/// could declare anything. A first segment found nowhere could be a top-level class of a library that
/// is not loaded, unless it is a plain variable (one segment, not called) or the library's
/// <c>uses(...)</c> annotation shows no library of that name is expected. And a member reached
/// through something <c>replaceable</c>, or of an <c>expandable</c> connector, is never judged: the
/// redeclared class or the connections decide what is there.</para>
///
/// <para>One finding per distinct name per class, at its first use.</para>
/// </remarks>
public sealed class UndeclaredNameAnalyzer : IGraphAnalyzer
{
    /// <inheritdoc/>
    public string Name => "UndeclaredNameAnalyzer";

    public IReadOnlyList<string> RuleIds { get; } = new[] { RuleIdsRef.UndeclaredName };

    public IEnumerable<Finding> Analyze(GraphAnalysisContext context)
    {
        var run = new Run(context.Graph, context.Settings.AcceptedToolBuiltIns);
        var models = context.Models;
        var perClass = new List<Finding>?[models.Count];
        Parallel.For(0, models.Count, i => perClass[i] = run.UndeclaredIn(models[i]));
        return perClass.Where(f => f is not null).SelectMany(f => f!).ToList();
    }

    private enum Outcome { Declared, Unknown, Undeclared }

    // The attributes of the predefined types (MLS §4.8), which a modification of one may name.
    private static class Predefined
    {
        private static readonly IReadOnlySet<string> Real = new HashSet<string>(StringComparer.Ordinal)
            { "value", "quantity", "unit", "displayUnit", "min", "max", "start", "fixed", "nominal", "unbounded", "stateSelect" };
        private static readonly IReadOnlySet<string> Integer = new HashSet<string>(StringComparer.Ordinal)
            { "value", "quantity", "min", "max", "start", "fixed" };
        private static readonly IReadOnlySet<string> Boolean = new HashSet<string>(StringComparer.Ordinal)
            { "value", "quantity", "start", "fixed" };
        private static readonly IReadOnlySet<string> Enumeration = Integer;

        /// <summary>The attributes of <paramref name="type"/>, or null when it is not a predefined type
        /// with attributes - including when it is a class that only shares one's name.</summary>
        public static IReadOnlySet<string>? Attributes(string? type) => type?.TrimStart('.') switch
        {
            "Real" => Real,
            "Integer" => Integer,
            "Boolean" or "String" => Boolean,
            "enumeration" => Enumeration,
            _ => null,
        };
    }

    // NamesTool: the message already says which tool the name belongs to.
    private readonly record struct Verdict(Outcome Outcome, string? Message = null, bool NamesTool = false)
    {
        public static readonly Verdict Declared = new(Outcome.Declared);
        public static readonly Verdict Unknown = new(Outcome.Unknown);
        public static Verdict Undeclared(string message) => new(Outcome.Undeclared, message);
    }

    // What one analysis shares between classes: the caches, and the answers about whole classes.
    private sealed class Run(DirectedGraph graph, IReadOnlyCollection<string> acceptedTools)
    {
        /// <summary>Whether the repository accepts <paramref name="tool"/>'s own functions and flags.</summary>
        public bool AcceptsBuiltInsOf(string tool) => acceptedTools.Contains(tool, StringComparer.OrdinalIgnoreCase);

        private readonly ClassElementResolver.InterfaceCache _interfaces = new();
        private readonly TypeResolver.AncestorCache _ancestors = new();
        private readonly ConcurrentDictionary<string, bool> _complete = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _expandable = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _replaceable = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string?> _predefinedRoot = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, IReadOnlySet<string>?> _literals = new(StringComparer.Ordinal);

        public List<Finding>? UndeclaredIn(ModelNode node)
        {
            if (node.IsParseFailurePlaceholder || node.IsExternalStub)
                return null;

            List<Finding>? findings = null;
            try
            {
                var (uses, isShortClass) = node.Definition.Borrow<(List<NameUse>, bool)>(
                    tree => (NameUseCollector.Collect(tree),
                             tree.class_definition() is [var c, ..] && c.class_specifier()?.short_class_specifier() is not null),
                    ([], false));
                if (uses.Count == 0)
                    return null;

                var scope = new Scope(this, node, ClassElementResolver.ReferencesIn(graph, node, _interfaces, _ancestors), isShortClass);
                var reported = new HashSet<string>(StringComparer.Ordinal);
                foreach (var use in uses)
                {
                    var key = use.Kind == NameUseKind.Modifier ? $"{use.Text}({string.Join('.', use.Path)})" : use.Text;
                    if (reported.Contains(key))
                        continue;
                    var verdict = scope.Judge(use);
                    if (verdict.Outcome != Outcome.Undeclared)
                        continue;
                    reported.Add(key);
                    (findings ??= []).Add(new Finding
                    {
                        RuleId = RuleIdsRef.UndeclaredName,
                        ModelId = node.Id,
                        ElementPath = use.Kind == NameUseKind.Modifier ? string.Join('.', use.Path) : use.Text,
                        Discriminator = use.Kind == NameUseKind.Modifier ? $"modifier:{use.Text}" : null,
                        Message = verdict.Message!,
                        LineNumber = use.Line,
                    });
                }
            }
            catch
            {
                // A class that cannot be analysed is skipped rather than breaking the run, as the
                // other analyses do; it reports nothing it is not sure of.
            }

            return findings;
        }

        public DirectedGraph Graph => graph;
        public TypeResolver.AncestorCache Ancestors => _ancestors;

        /// <summary>
        /// True when every member <paramref name="node"/> has is known: it is readable source, and every
        /// base it names resolves to a class of which the same is true.
        /// </summary>
        public bool IsComplete(ModelNode node) => IsComplete(node, []);

        private bool IsComplete(ModelNode node, HashSet<string> visiting)
        {
            if (_complete.TryGetValue(node.Id, out var known))
                return known;
            if (!visiting.Add(node.Id))
                return true;  // a cycle adds nothing a member could come from

            var complete = !node.IsExternalStub && !node.IsParseFailurePlaceholder;
            if (complete)
            {
                var imports = ModelicaGraph.ClassImports.For(node.Definition);
                foreach (var written in ModelicaGraph.ClassImports.BasesOf(node.Definition))
                {
                    if (TypeResolver.IsPredefined(written))
                        continue;
                    if (ClassElementResolver.ResolveBaseOf(graph, node, written, imports, _ancestors) is not { } baseNode
                        || !IsComplete(baseNode, visiting))
                    {
                        complete = false;
                        break;
                    }
                }
            }

            visiting.Remove(node.Id);
            _complete[node.Id] = complete;
            return complete;
        }

        /// <summary>
        /// Whether <paramref name="node"/> is declared <c>replaceable</c> (or <c>redeclare</c>) in the
        /// class around it: a class extending that one may replace it with one that has more.
        /// </summary>
        public bool IsReplaceableClass(ModelNode node) =>
            _replaceable.GetOrAdd(node.Id, id =>
                ModelicaName.EnclosingPackageOf(id) is { Length: > 0 } parentId
                && graph.GetNode<ModelNode>(parentId) is { } parent
                && _interfaces.MembersOf(graph, parent, includeProtected: true).TryGetValue(ModelicaName.LeafOf(id), out var member)
                && member.Element.Kind == ClassElementKind.Class
                && (member.Element.Prefixes.Contains("replaceable") || member.Element.Prefixes.Contains("redeclare")));

        /// <summary>
        /// The literals of an enumeration type written as one - <c>type E = enumeration(a, b)</c> - or
        /// null for anything else, including <c>enumeration(:)</c>, whose literals are not known.
        /// </summary>
        public IReadOnlySet<string>? LiteralsOf(ModelNode node) =>
            node.ClassType != "type"
                ? null
                : _literals.GetOrAdd(node.Id, _ => node.Definition.Borrow<IReadOnlySet<string>?>(
                    tree => tree.class_definition() is [var c, ..]
                            && c.class_specifier()?.short_class_specifier() is { } spec
                            && spec.GetChild(2)?.GetText() == "enumeration"
                            && spec.enum_list() is { } list
                        ? list.enumeration_literal().Select(l => l.IDENT().GetText()).ToHashSet(StringComparer.Ordinal)
                        : null,
                    null));

        /// <summary>
        /// The predefined type <paramref name="node"/> is underneath - <c>Real</c> for
        /// <c>type Voltage = Real(unit = "V")</c> and for <c>connector RealOutput = output Real</c>,
        /// <c>enumeration</c> for an enumeration type - or null for a class with members of its own.
        /// </summary>
        public string? PredefinedRootOf(ModelNode node) => PredefinedRootOf(node, 0);

        private string? PredefinedRootOf(ModelNode node, int depth)
        {
            if (_predefinedRoot.TryGetValue(node.Id, out var known))
                return known;

            string? root = null;
            var bases = ModelicaGraph.ClassImports.BasesOf(node.Definition);
            if (bases.Count == 0)
            {
                if (node.ClassType == "type"
                    && node.Definition.Borrow(
                        tree => tree.class_definition() is [var c, ..]
                                && c.class_specifier()?.short_class_specifier()?.GetChild(2)?.GetText() == "enumeration",
                        false))
                    root = "enumeration";
            }
            else if (depth < 32)
            {
                foreach (var written in bases)
                {
                    var name = written.TrimStart('.');
                    if (Predefined.Attributes(name) is not null)
                        root = name;
                    else if (ClassElementResolver.ResolveBaseOf(graph, node, written, ModelicaGraph.ClassImports.For(node.Definition), _ancestors) is { } baseNode
                             && baseNode.Id != node.Id)
                        root = PredefinedRootOf(baseNode, depth + 1);
                    if (root is not null)
                        break;
                }
            }

            _predefinedRoot[node.Id] = root;
            return root;
        }

        /// <summary>Whether <paramref name="node"/> or a class it extends is an expandable connector,
        /// whose members are whatever is connected to it.</summary>
        public bool IsExpandable(ModelNode node) => IsExpandable(node, []);

        private bool IsExpandable(ModelNode node, HashSet<string> visiting)
        {
            if (_expandable.TryGetValue(node.Id, out var known))
                return known;
            if (!visiting.Add(node.Id))
                return false;

            var expandable = node.Definition.Borrow(
                    tree => tree.class_definition() is [var c, ..] && c.class_prefixes()?.GetText().Contains("expandable") == true,
                    false)
                || ClassElementResolver.DirectBases(graph, node, _ancestors).Any(b => IsExpandable(b.Base, visiting));

            visiting.Remove(node.Id);
            _expandable[node.Id] = expandable;
            return expandable;
        }

        /// <summary>
        /// Whether a first segment that nothing declares could be a top-level class MLQT has not
        /// loaded. Not when it is a plain variable - no class is a value - and not when the library's
        /// <c>uses(...)</c> lists every library it expects and this is none of them.
        /// </summary>
        public bool CouldBeUnloadedLibrary(ModelNode node, string first, bool plainValue)
        {
            if (plainValue)
                return false;
            var root = graph.GetNode<ModelNode>(ModelicaName.RootLibraryOf(node.Id));
            return root?.Uses is not { } uses || uses.ContainsKey(first);
        }
    }

    // The lookups for the names of one class.
    private sealed class Scope(Run run, ModelNode node, ComponentReferences references, bool isShortClass)
    {
        public Verdict Judge(NameUse use) => use.Kind switch
        {
            NameUseKind.Value => Reference(use.Text, call: false),
            NameUseKind.Call => Reference(use.Text, call: true),
            NameUseKind.Type => TypeName(use.Text, isBase: false),
            NameUseKind.Base => TypeName(use.Text, isBase: true),
            NameUseKind.Import => Import(use.Text),
            NameUseKind.Modifier => Modifier(use),
            _ => Verdict.Unknown,
        };

        // A component reference, or the name a call calls - a function, a record, or a component of a
        // function type.
        private Verdict Reference(string text, bool call)
        {
            if (ClassElementResolver.ReferenceSegments(text) is not { } parsed)
                return Verdict.Unknown;
            var (segments, global) = parsed;
            // Global as well: the built-in functions are in the global scope, so `.sqrt` is sqrt.
            if (ModelicaLanguage.IsBuiltInName(segments[0]))
                return Verdict.Declared;
            if (!call && references.Resolve(text) is not null)
                return Verdict.Declared;
            if (call && references.QualifyingClass(text.TrimStart('.'), global) is not null)
                return Verdict.Declared;

            var verdict = Walk(segments, global, plainValue: !call && segments.Count == 1, text);

            // A tool's name that lookup bound part of, and then lost - or never bound at all: `Advanced.Translation.X` where
            // `import Modelica.Utilities.Strings.*` brings in MSL's own Strings.Advanced, which has
            // no Translation. The tool reads its flags whatever an import brings in, so the name is
            // the tool's - accepted where the tool is, and said to be its where it is not.
            if (verdict is { Outcome: Outcome.Undeclared, NamesTool: false } && ToolBuiltIns.ToolOf(text) is { } tool)
                return run.AcceptsBuiltInsOf(tool)
                    ? Verdict.Declared
                    : Verdict.Undeclared($"{verdict.Message}. '{text.TrimStart('.')}' is a built-in of {tool}, which this repository does not accept");
            return verdict;
        }

        private Verdict TypeName(string text, bool isBase)
        {
            var name = text.TrimStart('.');
            var global = text.StartsWith('.');
            if (TypeResolver.IsPredefined(name) || ModelicaLanguage.IsBuiltInName(name))
                return Verdict.Declared;
            if (isBase && ClassElementResolver.ResolveBaseOf(run.Graph, node, text, references.OwnImports, run.Ancestors) is not null)
                return Verdict.Declared;
            if (references.QualifyingClass(name, global) is not null)
                return Verdict.Declared;
            // A base spelled as the class's own name is a class extends, found through the enclosing
            // class's bases; ResolveBaseOf has answered that, and a miss there is not judged here.
            if (isBase && string.Equals(name, ModelicaName.LeafOf(node.Id), StringComparison.Ordinal))
                return Verdict.Unknown;
            if (ModelicaName.Segments(name) is not { Count: > 0 } segments)
                return Verdict.Unknown;

            return Walk(segments.ToList(), global, plainValue: false, text);
        }

        // An import names a class from the top: `A.B.C`, `X = A.B.C`, `A.B.*`, `A.B.{C, D}`.
        private Verdict Import(string import)
        {
            var statement = import.Trim();
            var eq = statement.IndexOf('=');
            if (eq >= 0)
                statement = statement[(eq + 1)..].Trim();

            List<string> targets;
            var list = statement.IndexOf(".{", StringComparison.Ordinal);
            if (list >= 0)
            {
                var package = statement[..list];
                targets = statement[(list + 2)..].TrimEnd('}').Split(',', StringSplitOptions.TrimEntries)
                    .Select(n => $"{package}.{n}").ToList();
            }
            else
            {
                targets = [statement.EndsWith(".*", StringComparison.Ordinal) ? statement[..^2] : statement];
            }

            foreach (var target in targets)
            {
                if (TypeResolver.ResolveGlobal(run.Graph, target, run.Ancestors) is not null)
                    continue;
                var segments = ModelicaName.Segments(target).ToList();
                if (segments.Count < 2
                    || TypeResolver.ResolveGlobal(run.Graph, segments[0], run.Ancestors) is not { } root)
                    continue;  // a library that is not loaded
                var verdict = FromClass(root, segments, 1, target);
                if (verdict.Outcome == Outcome.Undeclared)
                    return Verdict.Undeclared($"import {import}: {verdict.Message}");
            }
            return Verdict.Declared;
        }

        // `Resistor r(R = 1)`, `extends Base(x(start = 0))`: each name modified must be a member of the
        // type it is written on, followed down through the members before it.
        private Verdict Modifier(NameUse use)
        {
            var owner = use.OwnerType!;
            if (FirstNamesReplaceable(node, owner))
                return Verdict.Unknown;
            if (Predefined.Attributes(owner) is { } ownAttributes)
                return Attribute(ownAttributes, owner, use, 0);
            var type = use.OwnerIsBase
                ? ClassElementResolver.ResolveBaseOf(run.Graph, node, owner, references.OwnImports, run.Ancestors)
                : TypeResolver.ResolveWithInheritance(run.Graph, node.Id, owner.TrimStart('.'), references.OwnImports, run.Ancestors);
            if (type is null || run.IsReplaceableClass(type))
                return Verdict.Unknown;

            for (var i = 0; i < use.Path.Count; i++)
            {
                if (!Judgeable(type))
                    return Verdict.Unknown;
                // A type that is a predefined one underneath - `SI.Voltage`, `RealOutput` - has the
                // attributes of that type rather than members.
                if (run.PredefinedRootOf(type) is { } root)
                    return Predefined.Attributes(root) is { } attributes ? Attribute(attributes, root, use, i) : Verdict.Unknown;
                if (type.ClassType == "type")
                    return Verdict.Unknown;

                // Protected members included: modifying one is an error of its own, not a missing name.
                var members = references.Members(type, includeProtected: true);
                members.TryGetValue(use.Path[i], out var member);
                var nested = member is null || member.Element.Kind == ClassElementKind.Class
                    ? TypeResolver.MemberClass(run.Graph, type, use.Path[i], run.Ancestors)
                    : null;
                if (member is null && nested is null)
                    return i == use.Path.Count - 1
                        ? Verdict.Undeclared($"'{use.Path[i]}' is modified, but {Describe(type)} has no element of that name")
                        : Verdict.Unknown;  // reported at the shorter path
                if (i == use.Path.Count - 1)
                    return Verdict.Declared;
                if (member is not null && IsReplaceable(member))
                    return Verdict.Unknown;

                if (member is { Element.Kind: ClassElementKind.Component })
                {
                    if (FirstNamesReplaceable(run.Graph.GetNode<ModelNode>(member.OwnerId), member.Element.Type))
                        return Verdict.Unknown;
                    if (Predefined.Attributes(member.Element.Type) is { } attributes)
                        return Attribute(attributes, member.Element.Type!, use, i + 1);
                    type = references.TypeOf(member);
                }
                else
                {
                    type = nested;
                }
                if (type is null || run.IsReplaceableClass(type))
                    return Verdict.Unknown;
            }
            return Verdict.Declared;
        }

        // A modification of a predefined type's attribute, `Real x(start = 0)`: one more name, which
        // must be one of the type's attributes (MLS §4.8).
        private static Verdict Attribute(IReadOnlySet<string> attributes, string type, NameUse use, int index)
        {
            if (index != use.Path.Count - 1)
                return Verdict.Unknown;  // reported at the shorter path, or nonsense below an attribute
            return attributes.Contains(use.Path[index])
                ? Verdict.Declared
                : Verdict.Undeclared($"'{use.Path[index]}' is modified, but it is not an attribute of {type.TrimStart('.')} (MLS §4.8)");
        }

        // Finds the first segment that fails, as the resolver would have looked for it.
        private Verdict Walk(List<string> segments, bool global, bool plainValue, string text)
        {
            var first = segments[0];
            if (!global)
            {
                if (references.OwnMembers.TryGetValue(first, out var own))
                    return own.Element.Kind == ClassElementKind.Component
                        ? FromComponent(own, segments, 1, text)
                        : FromClassNamed(first, segments, global, text);

                if (references.ImportedComponent(references.OwnImports, first) is { } imported)
                    return FromComponent(imported.Element, segments, 1, text);

                var immediate = true;
                foreach (var enclosing in references.EnclosingScopes())
                {
                    var lendsAll = immediate && isShortClass;
                    immediate = false;
                    if (references.Members(enclosing, includeProtected: true).TryGetValue(first, out var found))
                    {
                        if (found.Element.Kind != ClassElementKind.Component)
                            break;
                        // A short class's modifications are written in the class that declares it, so
                        // that class's elements are all in scope for them - `function g = f(n = n)`.
                        if (found.Element.Variability != "constant" && !lendsAll)
                            return run.IsComplete(enclosing)
                                ? Verdict.Undeclared($"'{first}' is declared in {Describe(enclosing)}, which encloses this class, but only a constant of an enclosing class is visible here (MLS §5.3.1)")
                                : Verdict.Unknown;
                        return FromComponent(found, segments, 1, text);
                    }
                    if (references.ImportedComponent(ModelicaGraph.ClassImports.For(enclosing.Definition), first) is { } lent)
                        return FromComponent(lent.Element, segments, 1, text);
                }
            }

            if (references.QualifyingClass(first, global) is not null)
                return FromClassNamed(first, segments, global, text);

            // A tool's own function or flag, asked only now that nothing in the libraries declares it:
            // a class a library declares under the same name is what the name means.
            var tool = ToolBuiltIns.ToolOf(text);
            if (tool is not null && run.AcceptsBuiltInsOf(tool))
                return Verdict.Declared;

            // A tool's name is no library's, so it is not one that might be loaded later; a scope
            // that is not complete could still declare it.
            if (!FirstSegmentIsKnowable(first, global, plainValue || tool is not null))
                return Verdict.Unknown;
            if (tool is not null)
                return Verdict.Undeclared($"'{text.TrimStart('.')}' is not declared: it is a built-in of {tool}, which this repository does not accept, so other tools will not find it")
                    with { NamesTool = true };
            return Verdict.Undeclared(segments.Count == 1
                ? $"'{first}' is not declared: nothing of that name is declared in this class, inherited, imported or reachable from it"
                : $"'{first}' in '{text}' is not declared: nothing of that name is declared in this class, inherited, imported or reachable from it");
        }

        private Verdict FromClassNamed(string first, List<string> segments, bool global, string text)
        {
            if (references.QualifyingClass(first, global) is not { } named)
                return Verdict.Unknown;
            if (!global && references.OwnMembers.TryGetValue(first, out var own) && IsReplaceable(own))
                return Verdict.Unknown;
            return FromClass(named.Node, segments, 1, text);
        }

        // Each segment from `index` a member of the class before it.
        private Verdict FromClass(ModelNode scope, List<string> segments, int index, string text)
        {
            for (var i = index; i < segments.Count; i++)
            {
                // An enumeration's literals are no interface's members; they are read from its list.
                if (i == segments.Count - 1 && run.LiteralsOf(scope) is { } literals)
                    return literals.Contains(segments[i])
                        ? Verdict.Declared
                        : Verdict.Undeclared($"'{segments[i]}' in '{text}' is not declared: {Describe(scope)} has no literal of that name");
                if (!Judgeable(scope) || scope.ClassType == "type")
                    return Verdict.Unknown;
                var members = references.Members(scope, includeProtected: false);
                if (members.TryGetValue(segments[i], out var member) && member.Element.Kind == ClassElementKind.Component)
                    return FromComponent(member, segments, i + 1, text);
                if (member is not null && IsReplaceable(member))
                    return Verdict.Unknown;
                // A nested class is a node of its own, and one stored in a file of its own is in no
                // package's interface - the graph is where it is found.
                if (TypeResolver.MemberClass(run.Graph, scope, segments[i], run.Ancestors) is not { } next)
                    return member is null ? Missing(scope, segments, i, text) : Verdict.Unknown;
                if (run.IsReplaceableClass(next))
                    return Verdict.Unknown;
                scope = next;
            }
            return Verdict.Declared;
        }

        // Each segment from `index` a public member of the previous component's type.
        private Verdict FromComponent(ResolvedElement element, List<string> segments, int index, string text)
        {
            for (var i = index; i < segments.Count; i++)
            {
                if (IsReplaceable(element)
                    || FirstNamesReplaceable(run.Graph.GetNode<ModelNode>(element.OwnerId), element.Element.Type)
                    || references.TypeOf(element) is not { } type
                    || run.IsReplaceableClass(type)
                    || !Judgeable(type)
                    || type.ClassType == "type"
                    || run.PredefinedRootOf(type) is not null)
                    return Verdict.Unknown;
                if (!references.Members(type, includeProtected: false).TryGetValue(segments[i], out var member))
                    return Missing(type, segments, i, text);
                if (member.Element.Kind != ClassElementKind.Component)
                    return Verdict.Unknown;
                element = member;
            }
            return Verdict.Declared;
        }

        private Verdict Missing(ModelNode type, List<string> segments, int i, string text) =>
            references.Members(type, includeProtected: true).ContainsKey(segments[i])
                ? Verdict.Undeclared($"'{segments[i]}' in '{text}' is protected in {Describe(type)}, so it cannot be reached through a dot")
                : Verdict.Undeclared($"'{segments[i]}' in '{text}' is not declared: {Describe(type)} has no element of that name");

        private bool Judgeable(ModelNode type) => run.IsComplete(type) && !run.IsExpandable(type);

        // Whether a first segment found nowhere would have been found if it existed: every scope it was
        // looked for in is complete, no import that could bring it in is unresolved, and it cannot be a
        // library that is not loaded.
        private bool FirstSegmentIsKnowable(string first, bool global, bool plainValue)
        {
            if (run.CouldBeUnloadedLibrary(node, first, plainValue && !global))
                return false;
            if (global)
                return true;
            if (!run.IsComplete(node) || ImportCouldBind(references.OwnImports, first))
                return false;

            // Every enclosing class up to an encapsulated one, each loaded: a library loaded in part
            // (one file of it) leaves packages out of the graph, and what they declare would be missed.
            if (!ModelicaGraph.ClassImports.IsEncapsulated(node.Definition))
                foreach (var enclosingId in ModelicaName.EnclosingNamesOf(node.Id))
                {
                    if (run.Graph.GetNode<ModelNode>(enclosingId) is not { } enclosing
                        || !run.IsComplete(enclosing)
                        || ImportCouldBind(ModelicaGraph.ClassImports.For(enclosing.Definition), first))
                        return false;
                    if (ModelicaGraph.ClassImports.IsEncapsulated(enclosing.Definition))
                        break;
                }
            return true;
        }

        // An import that would bring in `first` if what it names were loaded and complete.
        private bool ImportCouldBind(IReadOnlyList<string> imports, string first)
        {
            foreach (var import in imports)
            {
                var statement = import.Trim();
                var eq = statement.IndexOf('=');
                if (eq >= 0)
                {
                    if (string.Equals(statement[..eq].Trim(), first, StringComparison.Ordinal))
                        return true;
                }
                else if (TypeResolver.IsWildcard(statement))
                {
                    var package = TypeResolver.ResolveGlobal(run.Graph, statement[..^2], run.Ancestors);
                    if (package is null || !run.IsComplete(package))
                        return true;
                }
                else
                {
                    // A qualified import of `first` that resolved would already have bound it, so
                    // one naming it here names something MLQT cannot see.
                    var list = statement.IndexOf(".{", StringComparison.Ordinal);
                    var named = list >= 0
                        ? statement[(list + 2)..].TrimEnd('}').Split(',', StringSplitOptions.TrimEntries)
                        : [ModelicaName.LeafOf(statement)];
                    if (named.Contains(first, StringComparer.Ordinal))
                        return true;
                }
            }
            return false;
        }

        private static bool IsReplaceable(ResolvedElement element) =>
            element.Element.Prefixes.Contains("replaceable") || element.Element.Prefixes.Contains("redeclare");

        // `Medium.ThermodynamicState` where `Medium` is a replaceable package of the declaring class:
        // the redeclared package decides what the type has.
        private bool FirstNamesReplaceable(ModelNode? owner, string? typeName)
        {
            if (owner is null || string.IsNullOrEmpty(typeName) || typeName.StartsWith('.'))
                return false;
            var first = ModelicaName.Segments(typeName).FirstOrDefault();
            // The class being checked through the table already read for it; the run's cache keeps
            // only classes that references pass through (B147).
            IReadOnlyDictionary<string, ResolvedElement> members =
                owner.Id == node.Id ? references.OwnMembers : references.Members(owner, includeProtected: true);
            return first is not null
                   && members.TryGetValue(first, out var member)
                   && member.Element.Kind == ClassElementKind.Class
                   && IsReplaceable(member);
        }

        private static string Describe(ModelNode type) => type.Id;
    }
}
