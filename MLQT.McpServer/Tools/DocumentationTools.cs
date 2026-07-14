using System.ComponentModel;
using Antlr4.Runtime.Tree;
using ModelContextProtocol.Server;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Tools for adding/changing a class's documentation surgically: its description string, a component's
/// description string, and the Documentation(info/revisions) annotation. Complements the read-only
/// get_class_documentation. Each validates, parse-checks with rollback, refuses read-only files and
/// refreshes dependencies; all support preview.
/// </summary>
[McpServerToolType]
public sealed class DocumentationTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public DocumentationTools(ILibraryDataService libraries, IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
    }

    [McpServerTool(Name = "set_class_description")]
    [Description("Set (or replace) a class's one-line description string, e.g. give 'model Foo' the " +
                "description 'Output the integral of the input'. This is the quoted string right after the " +
                "class name. Fails if the class has no long body (e.g. a short type alias) or the result " +
                "would not parse. Set preview=true to see the file text.")]
    public async Task<object> SetClassDescription(
        [Description("Fully-qualified class id.")] string classId,
        [Description("The description text (without quotes).")] string description,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrEmpty(description))
            return new ToolError("description must be non-empty.");

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var longSpec = ModelicaNav.LongSpec(ctx!.ClassCode);
        if (longSpec is null || longSpec.IDENT().Length == 0)
            return new ToolError($"'{classId}' has no long class body to describe (it may be a short class definition).");

        var code = ctx.ClassCode;
        var quoted = ModelicaNav.Quote(description);
        var sc = longSpec.string_comment();
        string newCode;
        if (sc is not null && sc.STRING().Length > 0)
            newCode = code[..sc.Start.StartIndex] + quoted + code[(sc.Stop.StopIndex + 1)..];
        else
        {
            var nameStop = longSpec.IDENT(0).Symbol.StopIndex;
            newCode = code[..(nameStop + 1)] + " " + quoted + code[(nameStop + 1)..];
        }

        return ToResult(classId, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newCode, preview, $"set description of '{classId}'"));
    }

    [McpServerTool(Name = "set_component_description")]
    [Description("Set (or replace) the description string of a component in a class, e.g. give 'Real k' " +
                "the description 'gain'. Fails if no such component exists or the result would not parse. " +
                "Set preview=true to see the file text.")]
    public async Task<object> SetComponentDescription(
        [Description("Fully-qualified class id containing the component.")] string classId,
        [Description("The component's name.")] string componentName,
        [Description("The description text (without quotes).")] string description,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (string.IsNullOrEmpty(description))
            return new ToolError("description must be non-empty.");

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var code = ctx!.ClassCode;
        var decl = ModelicaNav.FindComponent(code, componentName);
        if (decl is null)
            return new ToolError($"'{classId}' has no component named '{componentName}'.");

        var quoted = ModelicaNav.Quote(description);
        var sc = decl.comment()?.string_comment();
        string newCode;
        if (sc is not null && sc.STRING().Length > 0)
            newCode = code[..sc.Start.StartIndex] + quoted + code[(sc.Stop.StopIndex + 1)..];
        else
        {
            // Insert after the declaration (name/subscripts/modification), before any annotation.
            var declStop = decl.declaration().Stop.StopIndex;
            newCode = code[..(declStop + 1)] + " " + quoted + code[(declStop + 1)..];
        }

        return ToResult(classId, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newCode, preview, $"set description of '{componentName}'"));
    }

    [McpServerTool(Name = "set_class_documentation")]
    [Description("Set (or replace) a class's Documentation annotation — the rich HTML help shown in " +
                "Modelica tools. Provide info (the main documentation) and/or revisions (the change log) as " +
                "HTML strings, e.g. '<html><p>Integrates the input.</p></html>'. Whichever you omit is left " +
                "unchanged. Adds the annotation if the class has none. Read it back with " +
                "get_class_documentation. Fails if the class has no long body or the result would not parse.")]
    public async Task<object> SetClassDocumentation(
        [Description("Fully-qualified class id.")] string classId,
        [Description("The Documentation(info=...) HTML string. Omit to leave it unchanged.")] string? info = null,
        [Description("The Documentation(revisions=...) HTML string. Omit to leave it unchanged.")] string? revisions = null,
        [Description("Return the resulting file text without writing. Default false.")] bool preview = false)
    {
        if (info is null && revisions is null)
            return new ToolError("Provide info and/or revisions.");

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var (newCode, buildError) = BuildDocumentationEdit(ctx!.ClassCode, info, revisions);
        if (buildError is not null)
            return new ToolError($"'{classId}': {buildError}");

        return ToResult(classId, await ClassBodyEditor.ApplyAsync(
            _libraries, _resources, _session, ctx, newCode!, preview, $"set documentation of '{classId}'"));
    }

    private static (string? NewCode, string? Error) BuildDocumentationEdit(string code, string? info, string? revisions)
    {
        var longSpec = ModelicaNav.LongSpec(code);
        var composition = longSpec?.composition();
        if (longSpec is null || composition is null)
            return (null, "has no long class body to document (it may be a short class definition).");

        // Merge with any existing info/revisions (kept verbatim from source when not overridden).
        var docClassMod = FindDocumentationClassModification(composition);
        var newInfo = info is not null ? ModelicaNav.Quote(info) : GetArgValueRaw(docClassMod, "info", code);
        var newRevisions = revisions is not null ? ModelicaNav.Quote(revisions) : GetArgValueRaw(docClassMod, "revisions", code);

        var parts = new List<string>();
        if (newInfo is not null) parts.Add("info=" + newInfo);
        if (newRevisions is not null) parts.Add("revisions=" + newRevisions);
        var newDoc = "Documentation(" + string.Join(", ", parts) + ")";

        var annotation = composition.annotation().FirstOrDefault();
        if (annotation is not null)
        {
            var docArg = FindArgument(annotation.class_modification(), "Documentation");
            if (docArg is not null)
                return (code[..docArg.Start.StartIndex] + newDoc + code[(docArg.Stop.StopIndex + 1)..], null);

            var cm = annotation.class_modification();
            var at = cm.Start.StartIndex + 1; // after '('
            var hasArgs = cm.argument_list()?.argument().Length > 0;
            return (code[..at] + (hasArgs ? newDoc + ", " : newDoc) + code[at..], null);
        }

        // No class annotation yet — insert one before the class 'end'.
        var end = FindEndOffset(longSpec);
        if (end is null)
            return (null, "could not locate the end of the class.");
        var ws = end.Value;
        while (ws > 0 && (code[ws - 1] == ' ' || code[ws - 1] == '\t'))
            ws--;
        var endIndent = code[ws..end.Value];
        var prefix = ws > 0 && code[ws - 1] == '\n' ? string.Empty : "\n";
        return (code[..ws] + prefix + "annotation (" + newDoc + ");\n" + endIndent + code[end.Value..], null);
    }

    private static modelicaParser.Class_modificationContext? FindDocumentationClassModification(
        modelicaParser.CompositionContext composition)
    {
        var annotation = composition.annotation().FirstOrDefault();
        var docArg = annotation is null ? null : FindArgument(annotation.class_modification(), "Documentation");
        return docArg?.element_modification_or_replaceable()?.element_modification()?.modification()?.class_modification();
    }

    private static modelicaParser.ArgumentContext? FindArgument(modelicaParser.Class_modificationContext? cm, string name)
    {
        var argList = cm?.argument_list();
        if (argList is null)
            return null;
        foreach (var arg in argList.argument())
            if (arg.element_modification_or_replaceable()?.element_modification()?.name()?.GetText() == name)
                return arg;
        return null;
    }

    // The raw (quoted, source-verbatim) value of an argument's modification, or null.
    private static string? GetArgValueRaw(modelicaParser.Class_modificationContext? cm, string name, string code)
    {
        var arg = FindArgument(cm, name);
        var mod = arg?.element_modification_or_replaceable()?.element_modification()?.modification();
        if (mod is null)
            return null;
        var text = code[mod.Start.StartIndex..(mod.Stop.StopIndex + 1)].TrimStart();
        if (text.StartsWith("=", StringComparison.Ordinal))
            text = text[1..].TrimStart();
        return text.Length == 0 ? null : text;
    }

    private static int? FindEndOffset(modelicaParser.Long_class_specifierContext longSpec)
    {
        for (var i = 0; i < longSpec.ChildCount; i++)
            if (longSpec.GetChild(i) is ITerminalNode t && t.GetText() == "end")
                return t.Symbol.StartIndex;
        return null;
    }

    private static object ToResult(string classId, object editOutcome)
    {
        if (editOutcome is ToolError)
            return editOutcome;
        var r = (ClassEditResult)editOutcome;
        return new StructureEditResult(classId, r.FilePath, r.PreviewOnly, !r.PreviewOnly, r.AffectedCount, r.NewFileContent, null);
    }
}
