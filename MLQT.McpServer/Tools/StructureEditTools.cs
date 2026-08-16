using ModelicaGraph.Analysis;
using System.ComponentModel;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using ModelicaParser.DataTypes;
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
[McpServerToolType]
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

    [McpServerTool(Name = "add_component")]
    [Description("Add a component (a variable, parameter or connector instance) to a class, e.g. a " +
                "'Modelica.Blocks.Continuous.Integrator integrator1(k = 2)'. Provide the component's type " +
                "(a class id), a name, and optionally a modifier and a description. The modifier is a " +
                "comma-separated list like 'k = 2, T = 10' (wrapped automatically as name(k = 2, T = 10)); " +
                "use '= 5' for a plain binding value. Use visibility='protected' for a protected component " +
                "(a protected section is created if the class has none). Use prefix for Modelica keywords " +
                "such as 'parameter', 'constant', 'replaceable', 'final', 'inner'/'outer', 'flow'/'stream' " +
                "(space-separated, in Modelica order, e.g. 'replaceable parameter'). constrainedBy adds a " +
                "'constrainedby' clause (only with a replaceable prefix); condition makes the component " +
                "conditional ('if <expr>'). Restricted-class rules are enforced: a package accepts only " +
                "constants; a type has no components; a function's public components must be input/output " +
                "(locals go in protected); a record takes only public data (no protected/flow/stream/input/" +
                "output); a block's connectors must be causal. Fails if the name already exists or the result " +
                "would not parse. Preview available.")]
    public async Task<object> AddComponent(
        [Description("Fully-qualified id of the class to add the component to.")] string classId,
        [Description("The component's type — a class id (e.g. 'Modelica.Blocks.Continuous.Integrator') or a " +
                     "built-in like 'Real'.")]
        string type,
        [Description("The component's name (a valid Modelica identifier).")] string name,
        [Description("Optional modifier(s): a comma-separated list like 'k = 2, T = 10' (becomes " +
                     "name(k = 2, T = 10)); or a binding like '= 5'; or an already-parenthesised group " +
                     "'(k = 2)'. A lone value like '5' is treated as '= 5'.")]
        string? modifier = null,
        [Description("Optional description string.")] string? description = null,
        [Description("Optional // comment line to place above the component.")] string? comment = null,
        [Description("'public' (default) or 'protected' — which section to place the component in.")]
        string visibility = "public",
        [Description("Optional declaration prefix keyword(s), space-separated in Modelica order, emitted " +
                     "verbatim before the type. E.g. 'parameter', 'constant', 'replaceable', 'final', " +
                     "'inner', 'outer', 'flow', 'stream', 'replaceable parameter'. Do NOT put the type here.")]
        string? prefix = null,
        [Description("Optional constraining clause for a replaceable component, e.g. " +
                     "'Modelica.Media.Interfaces.PartialMedium' — emitted as 'constrainedby <value>'. Only " +
                     "valid when prefix contains 'replaceable'.")]
        string? constrainedBy = null,
        [Description("Optional condition making the component conditional, e.g. 'useHeatPort' — emitted as " +
                     "'if <value>'.")]
        string? condition = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
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
            return new ToolError($"'{classId}' already has a component named '{name}'. Use set_component_modifier or remove_component first.");

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

    [McpServerTool(Name = "remove_component")]
    [Description("Remove a component from a class by name. Handles both a component on its own line and one " +
                "of several declared together (e.g. 'Real a, b, c;'). Fails if no such component exists or " +
                "the result would not parse. Set preview=true to see the file text.")]
    public async Task<object> RemoveComponent(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("The name of the component to remove.")] string name,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var comp = ctx!.Layout.Components.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
        if (comp is null)
            return new ToolError($"'{classId}' has no component named '{name}'.");

        var code = ctx.ClassCode;
        string newClassCode;
        if (comp.SoleInClause)
        {
            // Remove the whole declaration line: from the start of its line through the terminating ';'.
            var lineStart = code.LastIndexOf('\n', comp.ClauseStart) + 1;
            var semicolon = code.IndexOf(';', comp.ClauseStop);
            if (semicolon < 0)
                return new ToolError("Could not find the end of the component declaration.");
            var removeEnd = semicolon + 1 < code.Length && code[semicolon + 1] == '\n' ? semicolon + 1 : semicolon;
            newClassCode = code[..lineStart] + code[(removeEnd + 1)..];
        }
        else
        {
            // Remove just this declaration from a shared clause, taking one adjacent comma with it.
            var commaBefore = code.LastIndexOf(',', comp.DeclStart - 1);
            if (commaBefore > comp.ClauseStart)
            {
                newClassCode = code[..commaBefore] + code[(comp.DeclStop + 1)..];
            }
            else
            {
                var commaAfter = code.IndexOf(',', comp.DeclStop);
                var afterComma = commaAfter + 1 < code.Length && code[commaAfter + 1] == ' ' ? commaAfter + 1 : commaAfter;
                newClassCode = code[..comp.DeclStart] + code[(afterComma + 1)..];
            }
        }

        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"remove component from '{classId}'"));
    }

    [McpServerTool(Name = "set_component_modifier")]
    [Description("Set (or clear) a component's modifier/binding, e.g. change 'integrator1' to " +
                "'integrator1(k = 2)' or set a parameter's value with '= 5'. The modifier is a " +
                "comma-separated list like 'k = 2, T = 10' (wrapped as name(k = 2, T = 10)), a binding like " +
                "'= 5', or a parenthesised group '(k = 2)'. Pass an empty modifier to remove an existing " +
                "one. Fails if no such component exists or the result would not parse. Preview available.")]
    public async Task<object> SetComponentModifier(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("The name of the component to modify.")] string name,
        [Description("The new modifier: a list like 'k = 2, T = 10', a binding '= 5', or '(k = 2)'. " +
                     "Empty string clears the modifier.")]
        string modifier,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
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

    [McpServerTool(Name = "add_extends")]
    [Description("Add an 'extends' (inheritance) clause to a class, e.g. 'extends " +
                "Modelica.Blocks.Interfaces.SISO'. Optionally set inherited defaults with a modifier — a " +
                "comma-separated list like 'k = 2, T = 10' (wrapped as (k = 2, T = 10)) or an already- " +
                "parenthesised group. Inserted at the top of the class. Fails if the result would not parse.")]
    public async Task<object> AddExtends(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("The base class to extend (a class id).")] string baseType,
        [Description("Optional modifier(s), e.g. 'k = 2, T = 10' or '(k = 2)'.")] string? modifier = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(baseType))
            return new ToolError("baseType is required.");
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var trimmedMod = modifier?.Trim();
        var mod = string.IsNullOrEmpty(trimmedMod)
            ? string.Empty
            : (trimmedMod.StartsWith("(", StringComparison.Ordinal) ? trimmedMod : "(" + trimmedMod + ")");
        var line = $"extends {baseType.Trim()}{mod};";
        var newClassCode = InsertElement(ctx!.ClassCode, ctx.Layout, line, atTop: true);

        string? note = TypeResolver.Resolve(_libraries.CombinedGraph, classId, baseType, null) is null
            ? $"Note: base class '{baseType}' does not resolve to a loaded class — check the name or load its library."
            : null;

        return ToResult(classId, note, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"add extends to '{classId}'"));
    }

    [McpServerTool(Name = "add_import")]
    [Description("Add an 'import' statement to a class, e.g. 'Modelica.Units.SI', 'SI = Modelica.Units.SI' " +
                "or 'Modelica.Constants.*'. Provide just the import target (no 'import' keyword). Inserted " +
                "at the top of the class. Fails if the result would not parse.")]
    public async Task<object> AddImport(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("The import target, e.g. 'Modelica.Units.SI', 'SI = Modelica.Units.SI', 'Modelica.Constants.*'.")]
        string import,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(import))
            return new ToolError("import is required.");
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var line = $"import {import.Trim()};";
        var newClassCode = InsertElement(ctx!.ClassCode, ctx.Layout, line, atTop: true);
        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"add import to '{classId}'"));
    }

    [McpServerTool(Name = "add_equation")]
    [Description("Add an equation to a class's equation section (creating the section if needed), e.g. " +
                "'y = k*x' or 'der(x) = u'. Do not include the trailing ';'. Only valid in a model, block or " +
                "class (a package, record, connector, function or type cannot contain equations). Fails if " +
                "the result would not parse. For connections use add_connection; for algorithm statements " +
                "use add_statement.")]
    public async Task<object> AddEquation(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("The equation, e.g. 'y = k*x' (no trailing ';').")] string equation,
        [Description("Optional // comment line to place above the equation.")] string? comment = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
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

    [McpServerTool(Name = "add_statement")]
    [Description("Add a statement to a class/function's algorithm section (creating the section if " +
                "needed), e.g. 'y := k*x'. Do not include the trailing ';'. Only valid in a model, block, " +
                "class or function (a package, record, connector or type cannot contain an algorithm). " +
                "Fails if the result would not parse.")]
    public async Task<object> AddStatement(
        [Description("Fully-qualified id of the class/function.")] string classId,
        [Description("The statement, e.g. 'y := k*x' (no trailing ';').")] string statement,
        [Description("Optional // comment line to place above the statement.")] string? comment = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrWhiteSpace(statement))
            return new ToolError("statement is required.");
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        if (!AlgorithmClassKinds.Contains(ctx!.Node.ClassType))
            return new ToolError($"A {ctx.Node.ClassType} cannot contain an algorithm section — statements are " +
                                 "only valid in a model, block, class or function.");

        var line = WithComment(EnsureSemicolon(statement), comment, ctx.Layout.Indent);
        var newClassCode = InsertIntoSection(ctx.ClassCode, ctx.Layout.AlgorithmAppendOffset, "algorithm", line, ctx.Layout.Indent, ctx.Layout.BodyEndOffset);
        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"add statement to '{classId}'"));
    }

    [McpServerTool(Name = "add_connection")]
    [Description("Add a connect(portA, portB) equation to a class, e.g. connect(sine1.y, integrator1.u). " +
                "Ports are component references (a connector on the class, or component.connector). Both " +
                "ports must exist and resolve to connectors, and their connector types must be compatible " +
                "(RealOutput to RealInput is fine; a signal port to a physical Pin is refused). If a type " +
                "cannot be resolved the compatibility check is skipped with a note. Only valid in a model, " +
                "block or class. If both connected components already have a Placement, a diagram Line is " +
                "added automatically — routed orthogonally between the two connector positions, leaving each " +
                "on its edge, and coloured by connector type. Fails if a port is missing/not a connector, " +
                "the connectors are incompatible, or would not parse.")]
    public async Task<object> AddConnection(
        [Description("Fully-qualified id of the class to add the connection to.")] string classId,
        [Description("One port, e.g. 'sine1.y' or a connector on the class like 'u'.")] string portA,
        [Description("The other port, e.g. 'integrator1.u'.")] string portB,
        [Description("Optional // comment line to place above the connection.")] string? comment = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
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

    [McpServerTool(Name = "remove_connection")]
    [Description("Remove a connect(a, b) equation from a class by its two ports (order-insensitive). Fails " +
                "if no matching connection exists. Set preview=true to see the file text.")]
    public async Task<object> RemoveConnection(
        [Description("Fully-qualified id of the class.")] string classId,
        [Description("One port of the connection, e.g. 'sine1.y'.")] string portA,
        [Description("The other port, e.g. 'integrator1.u'.")] string portB,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var a = portA.Trim();
        var b = portB.Trim();
        var conn = ctx!.Layout.Connections.FirstOrDefault(c =>
            (c.PortA == a && c.PortB == b) || (c.PortA == b && c.PortB == a));
        if (conn is null)
            return new ToolError($"'{classId}' has no connection between '{a}' and '{b}'.");

        var newClassCode = RemoveWholeLine(ctx.ClassCode, conn.Start, conn.Stop);
        return ToResult(classId, null, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newClassCode, preview, $"remove connection from '{classId}'"));
    }

    [McpServerTool(Name = "list_connections")]
    [Description("List the connect(a, b) equations declared in a class. Also lists base classes that " +
                "themselves contain connections (their connections are NOT merged in — query those base " +
                "classes directly if you need the full wiring picture). Read-only.")]
    public object ListConnections(
        [Description("Fully-qualified id of the class.")] string classId)
    {
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error!;

        var connections = ctx!.Layout.Connections.Select(c => new ConnectionView(c.PortA, c.PortB)).ToList();
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
    // be applied and null is returned (add_component already notes the unresolved type separately).
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

    // Remove the whole line spanning [start, stop] plus its terminating ';' and trailing newline.
    private static string RemoveWholeLine(string code, int start, int stop)
    {
        var lineStart = code.LastIndexOf('\n', start) + 1;
        var semicolon = code.IndexOf(';', stop);
        if (semicolon < 0)
            semicolon = stop;
        var removeEnd = semicolon + 1 < code.Length && code[semicolon + 1] == '\n' ? semicolon + 1 : semicolon;
        return code[..lineStart] + code[(removeEnd + 1)..];
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

    [McpServerTool(Name = "batch_edit")]
    [Description("Apply a sequence of surgical edits ATOMICALLY — all succeed or none do. Ideal for " +
                "building a whole model in one shot: e.g. add several components then connect them. Each " +
                "operation's 'op' is one of add_component, remove_component, set_component_modifier, " +
                "add_extends, add_import, add_equation, add_statement, add_connection, remove_connection, " +
                "with the same arguments as those tools (class_id plus the relevant fields). Operations run " +
                "in order and see earlier ones (so you can add a component and connect it in the same " +
                "batch). If any operation fails, every change is rolled back and the failing operation is " +
                "reported. Set preview=true to get the resulting files without keeping the changes.")]
    public async Task<object> BatchEdit(
        [Description("The operations to apply in order. Each: {op, classId, ...fields for that op}.")]
        IReadOnlyList<BatchOperation> operations,
        [Description("Apply then roll back, returning the resulting file contents without keeping them. Default false.")]
        bool preview = false)
    {
        if (operations is null || operations.Count == 0)
            return new ToolError("operations must be a non-empty array.");

        // Pre-resolve every target file and snapshot its content, and reject unknown op names up front.
        var snapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < operations.Count; i++)
        {
            var op = operations[i];
            if (!KnownOps.Contains(op.Op))
                return new ToolError($"Operation {i} has unknown op '{op.Op}'. Valid ops: {string.Join(", ", KnownOps)}.");
            var (ctx, error) = ClassBodyEditor.Open(_libraries, op.ClassId);
            if (error is not null)
                return new ToolError($"Operation {i} ({op.Op} on '{op.ClassId}'): {((ToolError)error).Error}");
            snapshots.TryAdd(ctx!.FilePath, await File.ReadAllTextAsync(ctx.FilePath));
        }

        // Apply in order; each op writes and reloads, so later ops see earlier ones.
        for (var i = 0; i < operations.Count; i++)
        {
            var result = await Dispatch(operations[i]);
            if (result is ToolError te)
            {
                await RollbackAsync(snapshots);
                return new ToolError(
                    $"Batch failed at operation {i} ({operations[i].Op} on '{operations[i].ClassId}'): {te.Error} " +
                    "All changes were rolled back.");
            }
        }

        if (preview)
        {
            var contents = snapshots.Keys.Select(p => new BatchFileChange(p, File.ReadAllText(p))).ToList();
            await RollbackAsync(snapshots);
            return new BatchEditResult(PreviewOnly: true, operations.Count, contents);
        }

        var files = snapshots.Keys.Select(p => new BatchFileChange(p, null)).ToList();
        return new BatchEditResult(PreviewOnly: false, operations.Count, files);
    }

    private static readonly HashSet<string> KnownOps = new(StringComparer.Ordinal)
    {
        "add_component", "remove_component", "set_component_modifier", "add_extends", "add_import",
        "add_equation", "add_statement", "add_connection", "remove_connection"
    };

    private Task<object> Dispatch(BatchOperation op) => op.Op switch
    {
        "add_component" => AddComponent(op.ClassId, op.Type ?? string.Empty, op.Name ?? string.Empty, op.Modifier, op.Description, op.Comment, op.Visibility ?? "public", op.Prefix, op.ConstrainedBy, op.Condition),
        "remove_component" => RemoveComponent(op.ClassId, op.Name ?? string.Empty),
        "set_component_modifier" => SetComponentModifier(op.ClassId, op.Name ?? string.Empty, op.Modifier ?? string.Empty),
        "add_extends" => AddExtends(op.ClassId, op.BaseType ?? string.Empty, op.Modifier),
        "add_import" => AddImport(op.ClassId, op.Import ?? string.Empty),
        "add_equation" => AddEquation(op.ClassId, op.Equation ?? string.Empty, op.Comment),
        "add_statement" => AddStatement(op.ClassId, op.Statement ?? string.Empty, op.Comment),
        "add_connection" => AddConnection(op.ClassId, op.PortA ?? string.Empty, op.PortB ?? string.Empty, op.Comment),
        "remove_connection" => RemoveConnection(op.ClassId, op.PortA ?? string.Empty, op.PortB ?? string.Empty),
        _ => Task.FromResult<object>(new ToolError($"Unknown operation '{op.Op}'."))
    };

    // Restore every file that was actually modified back to its snapshot, then reload/refresh.
    private async Task RollbackAsync(Dictionary<string, string> snapshots)
    {
        var affected = new List<string>();
        foreach (var (path, original) in snapshots)
        {
            if (!File.Exists(path) || File.ReadAllText(path) == original)
                continue; // untouched (e.g. an op failed before writing this file)
            await File.WriteAllTextAsync(path, original);
            affected.AddRange(await _libraries.ReloadFileAsync(path));
        }
        if (affected.Count > 0)
            await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);
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
