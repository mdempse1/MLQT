using System.Reflection;
using ModelicaGraph.Analysis;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Surgical, element-level edits to a class body — add/remove/modify individual components — so an agent
/// can build up a model incrementally without re-sending the whole class. Each edit is validated,
/// parse-checked with rollback, refuses read-only files, and refreshes dependencies. All support preview.
/// </summary>
[Obfuscation]
public sealed class StructureEditTools
{
    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public StructureEditTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    // The legal Modelica declaration prefixes for a component, in any combination the language allows. Used
    // to catch a mistyped prefix (or a type accidentally placed in the prefix field) with a clear error.
    private static readonly HashSet<string> ComponentPrefixKeywords = new(StringComparer.Ordinal)
    {
        "redeclare", "final", "inner", "outer", "replaceable",
        "flow", "stream", "discrete", "parameter", "constant", "input", "output",
    };

    // Modelica restricted-class rules: which class kinds may legally contain each kind of element. A class
    // kind is one of model/block/class/connector/record/function/package/type (ModelNode.ClassType).
    // Equations and connect() are only allowed in a model, block or (unrestricted) class; algorithm
    // statements additionally in a function; a package may hold only classes and constants; a type has no
    // body elements. These stop the surgical tools from producing structurally-illegal classes.
    private static readonly HashSet<string> EquationClassKinds = new(StringComparer.Ordinal) { "model", "block", "class" };
    private static readonly HashSet<string> AlgorithmClassKinds = new(StringComparer.Ordinal) { "model", "block", "class", "function" };

    internal async Task<object> AddComponent(
        string classId,
        string type,
        string name,
        string? modifier = null,
        string? description = null,
        string? comment = null,
        string visibility = "public",
        string? prefix = null,
        string? constrainedBy = null,
        string? condition = null,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "add a component") is { } readOnlyClass)
            return readOnlyClass;

        if (string.IsNullOrWhiteSpace(type))
            return new ToolError("type is required (a class id or built-in type).");
        if (string.IsNullOrWhiteSpace(name) || !IdentifierRegex.IsMatch(name))
            return new ToolError($"name '{name}' is not a valid Modelica identifier.");

        var isProtected = string.Equals(visibility?.Trim(), "protected", StringComparison.OrdinalIgnoreCase);
        if (!isProtected && !string.IsNullOrWhiteSpace(visibility) &&
            !string.Equals(visibility.Trim(), "public", StringComparison.OrdinalIgnoreCase))
            return new ToolError($"visibility must be 'public' or 'protected', not '{visibility}'.");

        // Validate the prefix keywords so a typo (or a type dropped into the prefix field) fails clearly.
        var prefixTokens = (prefix ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in prefixTokens)
            if (!ComponentPrefixKeywords.Contains(token))
                return new ToolError(
                    $"'{token}' is not a valid component prefix. Allowed (space-separated, in Modelica order): " +
                    "redeclare, final, inner, outer, replaceable, flow, stream, discrete, parameter, constant, " +
                    "input, output. The type goes in 'type', not 'prefix'.");

        var isReplaceable = prefixTokens.Contains("replaceable");
        var isConstant = prefixTokens.Contains("constant");
        var hasCausality = prefixTokens.Contains("input") || prefixTokens.Contains("output");
        var hasConnection = prefixTokens.Contains("flow") || prefixTokens.Contains("stream");
        if (!string.IsNullOrWhiteSpace(constrainedBy) && !isReplaceable)
            return new ToolError("constrainedBy (a constraining clause) is only valid for a replaceable " +
                                 "component — add 'replaceable' to prefix.");

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        if (ctx!.Layout.Components.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
            return new ToolError($"'{classId}' already has a component named '{name}'. Use mlqt_edit_class's set_component_modifier or mlqt_edit_class's remove_component first.");

        // Restricted-class rules for components: a type has no body; a package may hold only constants.
        if (ctx.Node.ClassType == "type")
            return new ToolError("A type cannot contain component declarations.");
        if (ctx.Node.ClassType == "package" && !isConstant)
            return new ToolError("A package may only contain classes and constants — a component added to a " +
                                 "package must be a constant (add 'constant' to prefix).");

        // A function's public components must be its inputs/outputs; protected components are locals.
        if (ctx.Node.ClassType == "function" && !isProtected && !hasCausality)
            return new ToolError("A public component of a function must be an input or an output (add 'input' " +
                                 "or 'output' to prefix); make it protected for a local variable.");

        // A record holds only public data: no protected section, and no causality/connection prefixes.
        if (ctx.Node.ClassType == "record")
        {
            if (isProtected)
                return new ToolError("A record has no protected section — all record members are public.");
            if (hasCausality || hasConnection)
                return new ToolError("A record component cannot have an input, output, flow or stream prefix.");
        }

        // A block's connector components must be causal: every variable of the connector is input/output.
        if (ctx.Node.ClassType == "block" && AcausalConnectorVariable(classId, type.Trim()) is { } acausalVar)
            return new ToolError($"A block's connectors must be causal, but the connector '{type.Trim()}' has " +
                                 $"the acausal variable '{acausalVar}'. Use a signal connector (e.g. RealInput/" +
                                 "RealOutput), give the variable an input/output prefix, or make the class a model.");

        // Assemble in grammar order: [prefix ]Type name[(mod)][ if cond][ constrainedby X][ "desc"];
        var prefixText = prefixTokens.Length > 0 ? string.Join(' ', prefixTokens) + " " : string.Empty;
        var nameWithMod = name + FormatModifier(modifier);
        var cond = string.IsNullOrWhiteSpace(condition) ? string.Empty : $" if {condition.Trim()}";
        var constraint = string.IsNullOrWhiteSpace(constrainedBy) ? string.Empty : $" constrainedby {constrainedBy.Trim()}";
        var desc = string.IsNullOrEmpty(description) ? string.Empty : $" \"{description.Replace("\"", "\\\"")}\"";
        var line = WithComment($"{prefixText}{type.Trim()} {nameWithMod}{cond}{constraint}{desc};", comment, ctx.Layout.Indent);
        var newClassCode = InsertComponentElement(ctx.ClassCode, ctx.Layout, line, isProtected);

        // Best-effort note if the type does not resolve to a loaded class (still allowed — may be added later).
        string? note = null;
        if (!TypeResolver.IsPredefined(type) && TypeResolver.Resolve(_libraries.CombinedGraph, classId, type, null) is null)
            note = $"Note: type '{type}' does not resolve to a loaded class — check the name or load its library.";

        return ToResult(classId, note, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"add component to '{classId}'"));
    }

    internal async Task<object> RemoveComponent(
        string classId,
        string name,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "remove a component") is { } readOnlyClass)
            return readOnlyClass;

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var comp = ctx!.Layout.Components.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        if (comp is null)
            return new ToolError($"'{classId}' has no component named '{name}'.");

        // The connects that name it go with it, as they do when a component is deleted in a diagram
        // editor: left behind, each names nothing, and the class no longer translates. One inside a
        // for or if goes too - it names the component whichever index or branch applies.
        var connects = ctx.Layout.Connections
            .Where(c => ComponentOf(c.PortA) == name || ComponentOf(c.PortB) == name)
            .ToList();

        // Each removal is made at offsets into the original text, last first, so the ones still to
        // do are untouched by those already done.
        var removals = connects
            .Select(c => (Start: c.Start, Remove: (Func<string, string?>)(text => RemoveWholeLine(text, c.Start, c.Semicolon))))
            .Append((Start: comp.ClauseStart, Remove: text => RemoveDeclaration(text, comp)))
            .OrderByDescending(r => r.Start);

        var newClassCode = ctx.ClassCode;
        foreach (var (_, remove) in removals)
        {
            if (remove(newClassCode) is not { } removed)
                return new ToolError("Could not find the end of the component declaration or of a connection to it.");
            newClassCode = removed;
        }

        var note = connects.Count == 0
            ? null
            : $"Also removed {connects.Count} connection(s) to '{name}': "
              + string.Join(", ", connects.Select(c => $"connect({c.PortA}, {c.PortB})"
                                                       + (c.IsNested ? $" (within {string.Join(" / ", c.Within)})" : "")))
              + ".";

        return ToResult(classId, note, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"remove component from '{classId}'"));
    }

    // A component's declaration: its whole line when the clause declares it alone, otherwise just its
    // declarator, taking one adjacent comma with it.
    private static string? RemoveDeclaration(string code, ClassBodyComponent comp)
    {
        if (comp.SoleInClause)
            return code.IndexOf(';', comp.ClauseStop) is var semicolon and >= 0
                ? RemoveWholeLine(code, comp.ClauseStart, semicolon)
                : null;

        var commaBefore = code.LastIndexOf(',', comp.DeclStart - 1);
        if (commaBefore > comp.ClauseStart)
            return code[..commaBefore] + code[(comp.DeclStop + 1)..];

        var commaAfter = code.IndexOf(',', comp.DeclStop);
        var afterComma = commaAfter + 1 < code.Length && code[commaAfter + 1] == ' ' ? commaAfter + 1 : commaAfter;
        return code[..comp.DeclStart] + code[(afterComma + 1)..];
    }

    // The component a connect port goes through: its first segment without subscripts - `r` in
    // `r.p`, `r[i].p` and `r`. A quoted name is kept whole, brackets and all.
    private static string ComponentOf(string port)
    {
        var first = ModelicaName.Segments(port)[0];
        if (first.StartsWith('\''))
            return first.IndexOf('\'', 1) is var close and > 0 ? first[..(close + 1)] : first;
        return first.IndexOf('[') is var bracket and >= 0 ? first[..bracket] : first;
    }

    internal async Task<object> SetComponentModifier(
        string classId,
        string name,
        string modifier,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "modify a component") is { } readOnlyClass)
            return readOnlyClass;

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var comp = ctx!.Layout.Components.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        if (comp is null)
            return new ToolError($"'{classId}' has no component named '{name}'.");

        var code = ctx.ClassCode;
        var formatted = FormatModifier(modifier);
        string newClassCode;
        if (comp.ModStart is { } ms && comp.ModStop is { } me)
        {
            // Replace the existing modification span (which starts at '=' or '('); trim a now-orphaned space.
            var start = ms;
            if (formatted.Length == 0 && start > 0 && code[start - 1] == ' ')
                start -= 1;
            newClassCode = code[..start] + (formatted.Length == 0 ? string.Empty : formatted.TrimStart()) + code[(me + 1)..];
        }
        else
        {
            if (formatted.Length == 0)
                return new ToolError($"Component '{name}' has no modifier to clear.");
            newClassCode = code.Insert(comp.BindingInsertOffset, formatted);
        }

        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"modify component in '{classId}'"));
    }

    internal async Task<object> AddEquation(
        string classId,
        string equation,
        string? comment = null,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "add an equation") is { } readOnlyClass)
            return readOnlyClass;

        if (string.IsNullOrWhiteSpace(equation))
            return new ToolError("equation is required.");
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        if (!EquationClassKinds.Contains(ctx!.Node.ClassType))
            return new ToolError($"A {ctx.Node.ClassType} cannot contain an equation section — equations are " +
                                 "only valid in a model, block or class.");

        var line = WithComment(EnsureSemicolon(equation), comment, ctx.Layout.Indent);
        var newClassCode = InsertIntoSection(ctx.ClassCode, ctx.Layout.EquationAppendOffset, "equation", line, ctx.Layout.Indent, ctx.Layout.BodyEndOffset);
        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"add equation to '{classId}'"));
    }

    internal async Task<object> AddConnection(
        string classId,
        string portA,
        string portB,
        string? comment = null,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "add a connection") is { } readOnlyClass)
            return readOnlyClass;

        if (string.IsNullOrWhiteSpace(portA) || string.IsNullOrWhiteSpace(portB))
            return new ToolError("Both portA and portB are required.");

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        if (!EquationClassKinds.Contains(ctx!.Node.ClassType))
            return new ToolError($"A {ctx.Node.ClassType} cannot contain a connection — connect() equations " +
                                 "are only valid in a model, block or class.");

        var a = portA.Trim();
        var b = portB.Trim();
        var notes = new List<string>();

        if (CheckPort(classId, a, notes) is { } errA)
            return errA;
        if (CheckPort(classId, b, notes) is { } errB)
            return errB;

        // Compatibility (only when both connector types resolved).
        var resA = ConnectorCompatibility.ResolvePort(_libraries, classId, a);
        var resB = ConnectorCompatibility.ResolvePort(_libraries, classId, b);
        if (resA.Connector is { } ca && resB.Connector is { } cb)
        {
            var sigA = ConnectorCompatibility.Signature(_libraries, ca);
            var sigB = ConnectorCompatibility.Signature(_libraries, cb);
            if (sigA is not null && sigB is not null && !ConnectorCompatibility.SignaturesCompatible(sigA, sigB))
                return new ToolError(
                    $"Incompatible connectors: '{a}' is a '{ca.Id}' but '{b}' is a '{cb.Id}'. connect() requires " +
                    "matching connector types (differences in input/output are fine, but the connectors must " +
                    "otherwise be the same shape).");
        }

        var line = WithComment($"connect({a}, {b});", comment, ctx!.Layout.Indent);
        var newClassCode = InsertIntoSection(ctx.ClassCode, ctx.Layout.EquationAppendOffset, "equation", line, ctx.Layout.Indent, ctx.Layout.BodyEndOffset);
        // If both connected components are already positioned, draw the connection on the diagram layer.
        newClassCode = ConnectionLineAnnotator.Annotate(_libraries, classId, newClassCode);
        var note = notes.Count > 0 ? string.Join(" ", notes) : null;
        return ToResult(classId, note, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"add connection to '{classId}'"));
    }

    // Validate a port exists and is a connector. Returns a ToolError to refuse, or null (collecting a
    // note when the type could not be resolved so the caller proceeds unverified).
    private ToolError? CheckPort(string classId, string port, List<string> notes)
    {
        var res = ConnectorCompatibility.ResolvePort(_libraries, classId, port);
        if (res.Error is not null)
            return new ToolError($"Port '{port}': {res.Error}");
        if (res.Note is not null)
        {
            notes.Add($"Note: {res.Note}; connector compatibility for port '{port}' was not verified.");
            return null;
        }
        if (res.Connector is { } c && c.ClassType != "connector")
            return new ToolError($"Port '{port}' resolves to '{c.Id}', which is a {c.ClassType}, not a connector.");
        return null;
    }

    internal async Task<object> RemoveConnection(
        string classId,
        string portA,
        string portB,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "remove a connection") is { } readOnlyClass)
            return readOnlyClass;

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var a = portA.Trim();
        var b = portB.Trim();
        var conn = ctx!.Layout.Connections.FirstOrDefault(c =>
            (c.PortA == a && c.PortB == b) || (c.PortA == b && c.PortB == a));
        if (conn is null)
            return new ToolError($"'{classId}' has no connection between '{a}' and '{b}'.");

        if (RemoveWholeLine(ctx.ClassCode, conn.Start, conn.Semicolon) is not { } newClassCode)
            return new ToolError("Could not find the end of the connect statement.");

        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"remove connection from '{classId}'"));
    }

    internal object ListConnections(
        string classId)
    {
        // Read, not Open: listing writes nothing, so a read-only class answers like any other.
        var (layout, error) = ClassBodyEditor.Read(_libraries, classId);
        if (error is not null)
            return error;

        var connections = layout!.Connections.Select(ConnectionView.Of).ToList();
        return new ConnectionsResult(classId, connections, CollectBasesWithConnections(classId));
    }

    // Base classes (transitive) whose own bodies contain connections.
    private List<string> CollectBasesWithConnections(string classId)
    {
        var result = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        void Walk(string id)
        {
            if (!visited.Add(id))
                return;
            var tree = _libraries.GetModelById(id)?.Definition.EnsureParsed();
            if (tree is null)
                return;
            var iface = ClassInterfaceExtractor.Extract(tree);
            var imports = iface.Elements.Where(e => e.Kind == ClassElementKind.Import).Select(e => e.Name).ToList();
            foreach (var ext in iface.Elements.Where(e => e.Kind == ClassElementKind.Extends))
            {
                var baseNode = TypeResolver.Resolve(_libraries.CombinedGraph, id, ext.Type, imports);
                if (baseNode is null)
                    continue;
                if (ClassBodyLocator.Analyze(baseNode.Definition.ModelicaCode ?? string.Empty).Connections.Count > 0
                    && !result.Contains(baseNode.Id))
                    result.Add(baseNode.Id);
                Walk(baseNode.Id);
            }
        }

        Walk(classId);
        return result;
    }

    // Insert a new element line: at the top of the public section (extends/imports), appended after the
    // last element, or — for an empty class — before the class end.
    private static string InsertElement(string code, ClassBodyLayout layout, string line, bool atTop)
    {
        if (atTop && layout.FirstPublicElementOffset is int off)
            return code.Insert(off, $"{line}\n{layout.Indent}");
        if (layout.FirstPublicElementOffset is not null) // has elements: append after the last one
            return code.Insert(layout.PublicAppendOffset, $"\n{layout.Indent}{line}");
        return InsertBeforeEnd(code, layout.BodyEndOffset, $"{layout.Indent}{line}");
    }

    // For the block restricted-class rule: if 'type' resolves to a connector that has an acausal variable
    // (one with neither an input nor an output prefix), returns that variable's name; otherwise null. Only
    // composite connectors carry named variables — an alias connector (e.g. RealInput = input Real) has
    // none, so it passes. When the type does not resolve (e.g. its library is not loaded) the rule cannot
    // be applied and null is returned (mlqt_edit_class's add_component already notes the unresolved type separately).
    private string? AcausalConnectorVariable(string classId, string type)
    {
        var typeNode = TypeResolver.Resolve(_libraries.CombinedGraph, classId, type, null);
        if (typeNode is null || typeNode.ClassType != "connector")
            return null;
        return ClassElementResolver
            .Collect(_libraries.CombinedGraph, typeNode, includeProtected: false, includeInherited: true)
            .FirstOrDefault(m => m.Element.Kind == ClassElementKind.Component &&
                                 string.IsNullOrEmpty(m.Element.Causality))?.Element.Name;
    }

    // Insert a component element into the public or protected section, creating a protected section when
    // one is requested but does not yet exist (placed after the public elements, before any equations).
    private static string InsertComponentElement(string code, ClassBodyLayout layout, string line, bool isProtected)
    {
        if (!isProtected)
            return InsertElement(code, layout, line, atTop: false);

        var indent = layout.Indent;
        if (layout.ProtectedAppendOffset is int off) // append into the existing protected section
            return code.Insert(off, $"\n{indent}{line}");
        if (layout.FirstPublicElementOffset is not null) // new protected section after the public elements
            return code.Insert(layout.PublicAppendOffset, $"\nprotected\n{indent}{line}");
        return InsertBeforeEnd(code, layout.BodyEndOffset, $"protected\n{indent}{line}"); // class has no elements yet
    }

    // Append 'line' (which ends in ';') to an existing section, or create the section before the class end.
    private static string InsertIntoSection(string code, int? appendOffset, string keyword, string line, string indent, int bodyEnd)
    {
        if (appendOffset is int off)
            return code.Insert(off, $"\n{indent}{line}");
        return InsertBeforeEnd(code, bodyEnd, $"{keyword}\n{indent}{line}");
    }

    // Insert a block immediately before the class's 'end', keeping 'end' on its own line whether or not
    // it already was (the append offsets can leave 'end' sharing a line with the previous statement).
    private static string InsertBeforeEnd(string code, int bodyEnd, string block)
    {
        var ws = bodyEnd;
        while (ws > 0 && (code[ws - 1] == ' ' || code[ws - 1] == '\t'))
            ws--;
        var endIndent = code[ws..bodyEnd];
        var prefix = ws > 0 && code[ws - 1] == '\n' ? string.Empty : "\n";
        return code[..ws] + prefix + block + "\n" + endIndent + code[bodyEnd..];
    }

    /// <summary>
    /// Removes the whole line spanning <paramref name="start"/>..<paramref name="semicolon"/>, including
    /// that terminating <c>;</c> and the newline after it, and returns null when there is no
    /// <c>;</c> at <paramref name="semicolon"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>The one place a line is deleted from a class, for both callers.</b> Removing a
    /// component that is alone on its line used to compute the same three offsets for itself, seven
    /// lines away from this - so the arithmetic that decides which characters of a user's file
    /// disappear existed twice in one file, and a fix to either was a fix to one of them (B274).
    /// Its extra behaviour is kept rather than lost: it reported an error when there was no
    /// semicolon where this fell back to <c>stop</c>, so this now answers null and lets each caller
    /// say what that means.</para>
    ///
    /// <para><b>The caller says where the semicolon is.</b> This searched for the first <c>;</c>
    /// after the statement's span, and a connect's span ends at its closing parenthesis, before its
    /// description: <c>connect(a, b) "a; b";</c> was cut inside the string, the parse check refused
    /// the result, and a connection that was there could not be removed. A connect's comes from the
    /// tree (<c>ConnectClause.Semicolon</c>); a component's description and annotation are inside its
    /// clause, so the first <c>;</c> after the clause is its own.</para>
    ///
    /// <para>The contract the tests hold it to is the whole of it: <b>what is left is the original
    /// lines minus exactly one</b>. That is what a wrong offset here breaks - by swallowing the line
    /// above, by leaving a blank line behind, or by eating the newline and merging two lines - and
    /// none of those change whether the removed text is still present, which is all the tests before
    /// them asked.</para>
    ///
    /// <para><b>"The line" is the statement's line only when the statement has it to itself</b>
    /// (B312). With another statement before it on the line (<c>Real x; Real y;</c>) or after it
    /// (<c>connect(a, b); y = 2;</c>) only the statement goes, with the blanks that separated it from
    /// its neighbour - cutting from the line start deleted the neighbour as well, and the result
    /// still parsed, so the parse check passed and the user's code was silently lost.</para>
    /// </remarks>
    private static string? RemoveWholeLine(string code, int start, int semicolon)
    {
        var lineStart = code.LastIndexOf('\n', start) + 1;
        if (semicolon < start || semicolon >= code.Length || code[semicolon] != ';')
            return null;

        var lineEnd = code.IndexOf('\n', semicolon);
        if (lineEnd < 0)
            lineEnd = code.Length;
        var aloneBefore = string.IsNullOrWhiteSpace(code[lineStart..start]);
        var aloneAfter = string.IsNullOrWhiteSpace(code[(semicolon + 1)..lineEnd]);

        if (aloneBefore && aloneAfter)
        {
            var removeEnd = semicolon + 1 < code.Length && code[semicolon + 1] == '\n' ? semicolon + 1 : semicolon;
            return code[..lineStart] + code[(removeEnd + 1)..];
        }

        if (!aloneBefore)
        {
            // Something precedes it: take the blanks before the statement, keep everything after.
            var cut = start;
            while (cut > lineStart && IsBlank(code[cut - 1]))
                cut--;
            return code[..cut] + code[(semicolon + 1)..];
        }

        // Alone before, something after: keep the indentation, take the blanks after the semicolon.
        var resume = semicolon + 1;
        while (resume < lineEnd && IsBlank(code[resume]))
            resume++;
        return code[..start] + code[resume..];

        static bool IsBlank(char c) => c is ' ' or '\t';
    }

    private static string EnsureSemicolon(string text)
    {
        var t = text.Trim();
        return t.EndsWith(";", StringComparison.Ordinal) ? t : t + ";";
    }

    // Prepend a single-line // comment above 'line' (indented to match), or return 'line' unchanged.
    private static string WithComment(string line, string? comment, string indent)
    {
        if (string.IsNullOrWhiteSpace(comment))
            return line;
        var oneLine = comment.Replace("\r", " ").Replace("\n", " ").Trim();
        return $"// {oneLine}\n{indent}{line}";
    }

    // Normalise the modifier field into the text that follows a component name:
    //   'k=2, r=34'  -> '(k=2, r=34)'   a modifier list is wrapped in parentheses
    //   '(k=2)'      -> '(k=2)'          an explicit modifier group is used as-is
    //   '= 5' / ':=' -> ' = 5'           an explicit binding is kept (with a leading space)
    //   '5'          -> ' = 5'           a bare value becomes a binding
    private static string FormatModifier(string? modifier)
    {
        var m = modifier?.Trim();
        if (string.IsNullOrEmpty(m))
            return string.Empty;
        if (m.StartsWith("(", StringComparison.Ordinal))
            return m;
        if (m.StartsWith(":=", StringComparison.Ordinal) || m.StartsWith("=", StringComparison.Ordinal))
            return " " + m;
        if (m.Contains('='))
            return "(" + m + ")"; // one or more 'name=value' modifiers -> (…)
        return " = " + m;         // a lone value is a binding
    }

    private static object ToResult(string classId, string? note, object editOutcome)
    {
        if (editOutcome is ToolError)
            return editOutcome;
        var r = (ClassEditResult)editOutcome;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, note);
    }
}
