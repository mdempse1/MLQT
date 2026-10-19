using System.ComponentModel;
using System.Reflection;
using ModelContextProtocol.Server;
using ModelicaParser.Helpers;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// <c>mlqt_edit_class</c>: every change to part of one class, in one call. Its <c>replace</c> operation is
/// the text patch an agent otherwise makes with its own file tools - in the 2026-10-18 model-building
/// sessions, scripted find-and-replace on <c>.mo</c> files was the largest single way round MLQT
/// (B503). The other operations are the element, documentation and suppression edits each done by an
/// mlqt_ tool of the same name, which this calls rather than repeats.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class EditClassTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;
    private readonly StructureEditTools _structure;
    private readonly DocumentationTools _documentation;
    private readonly DiagramTools _diagram;
    private readonly SuppressionTools _suppression;
    private readonly SpellingTools _spelling;

    public EditClassTools(
        ILibraryDataService libraries,
        IRepositoryService repositories,
        ICustomDictionaryService customDictionary,
        IDictionaryManagerService dictionaryManager,
        IExternalResourceService resources,
        SessionState session)
    {
        _libraries = libraries;
        _resources = resources;
        _session = session;
        _structure = new StructureEditTools(libraries, resources, session);
        _documentation = new DocumentationTools(libraries, resources, session);
        _diagram = new DiagramTools(libraries, resources, session);
        _suppression = new SuppressionTools(libraries, resources, session);
        _spelling = new SpellingTools(libraries, repositories, customDictionary, dictionaryManager, resources, session);
    }

    /// <summary>Each operation and the fields it must be given; the error for a missing one lists them.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> Operations = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["replace"] = ["old", "new", "all?"],
        ["add_component"] = ["type", "name"],
        ["remove_component"] = ["name"],
        ["set_component_modifier"] = ["name", "modifier"],
        ["add_extends"] = ["baseType"],
        ["add_import"] = ["import"],
        ["add_equation"] = ["equation"],
        ["add_statement"] = ["statement"],
        ["add_connection"] = ["portA", "portB"],
        ["remove_connection"] = ["portA", "portB"],
        ["set_component_placement"] = ["name", "x1", "y1", "x2", "y2"],
        ["set_class_description"] = ["description"],
        ["set_component_description"] = ["name", "description"],
        ["set_class_documentation"] = ["info or revisions"],
        ["suppress_rule"] = ["ruleId"],
        ["accept_spelling"] = ["word"],
        ["correct_spelling"] = ["word", "newWord"],
    };

    [McpServerTool(Name = "mlqt_edit_class", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Change part of one Modelica class with a list of operations, applied in order and all or none. " +
                 "The usual one is replace: {\"op\":\"replace\",\"old\":\"y = k*u;\",\"new\":\"y = k*u + offset;\"} " +
                 "swaps text that must occur exactly once in the class, as written in its file - copy it from " +
                 "mlqt_get_class_source with includeAnnotations=true; with \"all\":true it swaps every occurrence " +
                 "(a name used throughout the class), and says how many. The others take the fields of the mlqt_ " +
                 "tool of the same name: add_component{type,name,modifier?,description?,visibility?}, " +
                 "remove_component{name}, set_component_modifier{name,modifier}, add_extends{baseType}, " +
                 "add_import{import}, add_equation{equation}, add_statement{statement}, add_connection{portA,portB} " +
                 "(refuses incompatible connectors), remove_connection{portA,portB}, " +
                 "set_component_placement{name,x1,y1,x2,y2,rotation?}, set_class_description{description}, " +
                 "set_component_description{name,description}, set_class_documentation{info?,revisions?}, " +
                 "suppress_rule{ruleId,component?,reason?}, accept_spelling{word,reason?}, " +
                 "correct_spelling{word,newWord}. Each result is parse-checked before it is written; if any " +
                 "operation fails, nothing is changed. preview=true returns the file without keeping it.")]
    public async Task<object> EditClass(
        [Description("Fully-qualified id of the class to change.")] string classId,
        [Description("The operations, in order. Each is {op, ...its fields}.")]
        IReadOnlyList<ClassEditOperation> operations,
        [Description("Apply, return the resulting file and undo it. Default false.")] bool preview = false)
    {
        // First, as every editing tool does: a read-only class is refused whatever was asked of it.
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "edit this class") is { } readOnlyClass)
            return readOnlyClass;
        if (operations is null || operations.Count == 0)
            return new ToolError("operations must be a non-empty array.");

        // Everything an operation lacks is reported before anything is written.
        for (var i = 0; i < operations.Count; i++)
        {
            var name = OpName(operations[i].Op);
            if (!Operations.TryGetValue(name, out var fields))
                return new ToolError($"Operation {i} has unknown op '{operations[i].Op}'. " +
                                     $"Valid ops: {string.Join(", ", Operations.Keys)}.");
            if (Missing(name, operations[i]) is { } missing)
                return new ToolError($"Operation {i} ({name}) needs {missing}. It takes: {string.Join(", ", fields)}.");
        }

        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;
        var snapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await EditRollback.SnapshotAsync(snapshots, ctx!.FilePath);

        var notes = new List<string>();
        for (var i = 0; i < operations.Count; i++)
        {
            var result = await Dispatch(classId, operations[i]);
            if (result is ToolError te)
            {
                await EditRollback.RestoreAsync(snapshots, _libraries, _resources, _session);
                return new ToolError($"Operation {i} ({OpName(operations[i].Op)}) failed: {te.Error} Nothing was changed.");
            }
            if (result is StructureEditResult { Note: { Length: > 0 } note })
                notes.Add($"Operation {i}: {note}");
        }

        string? content = null;
        if (preview)
        {
            content = ModelicaFileEncoding.ReadAllTextOnly(ctx.FilePath);
            await EditRollback.RestoreAsync(snapshots, _libraries, _resources, _session);
        }
        return new EditClassResult(classId, ctx.FilePath, preview, operations.Count, content, notes);
    }

    // The ops are named after the tools that do the same edit, so the tool's own name is accepted too,
    // and accept_spelling_in_class for accept_spelling.
    private static string OpName(string? op)
    {
        var name = op is not null && op.StartsWith("mlqt_", StringComparison.Ordinal) ? op["mlqt_".Length..] : op ?? string.Empty;
        return name == "accept_spelling_in_class" ? "accept_spelling" : name;
    }

    private static string? Missing(string op, ClassEditOperation o)
    {
        static bool Blank(string? s) => string.IsNullOrWhiteSpace(s);
        var missing = op switch
        {
            "replace" => string.IsNullOrEmpty(o.Old) ? "old" : o.New is null ? "new" : null,
            "add_component" => Blank(o.Type) ? "type" : Blank(o.Name) ? "name" : null,
            "remove_component" => Blank(o.Name) ? "name" : null,
            "set_component_modifier" => Blank(o.Name) ? "name" : o.Modifier is null ? "modifier" : null,
            "add_extends" => Blank(o.BaseType) ? "baseType" : null,
            "add_import" => Blank(o.Import) ? "import" : null,
            "add_equation" => Blank(o.Equation) ? "equation" : null,
            "add_statement" => Blank(o.Statement) ? "statement" : null,
            "add_connection" or "remove_connection" => Blank(o.PortA) ? "portA" : Blank(o.PortB) ? "portB" : null,
            "set_component_placement" => Blank(o.Name) ? "name"
                : o.X1 is null || o.Y1 is null || o.X2 is null || o.Y2 is null ? "x1, y1, x2 and y2" : null,
            "set_class_description" => Blank(o.Description) ? "description" : null,
            "set_component_description" => Blank(o.Name) ? "name" : Blank(o.Description) ? "description" : null,
            "set_class_documentation" => o.Info is null && o.Revisions is null ? "info or revisions" : null,
            "suppress_rule" => Blank(o.RuleId) ? "ruleId" : null,
            "accept_spelling" => Blank(o.Word) ? "word" : null,
            "correct_spelling" => Blank(o.Word) ? "word" : string.IsNullOrEmpty(o.NewWord) ? "newWord" : null,
            _ => null,
        };
        return missing;
    }

    private Task<object> Dispatch(string classId, ClassEditOperation o) => OpName(o.Op) switch
    {
        "replace" => Replace(classId, o.Old!, o.New!, o.All == true),
        "add_component" => _structure.AddComponent(classId, o.Type!, o.Name!, o.Modifier, o.Description, o.Comment,
            o.Visibility ?? "public", o.Prefix, o.ConstrainedBy, o.Condition),
        "remove_component" => _structure.RemoveComponent(classId, o.Name!),
        "set_component_modifier" => _structure.SetComponentModifier(classId, o.Name!, o.Modifier!),
        "add_extends" => _structure.AddExtends(classId, o.BaseType!, o.Modifier),
        "add_import" => _structure.AddImport(classId, o.Import!),
        "add_equation" => _structure.AddEquation(classId, o.Equation!, o.Comment),
        "add_statement" => _structure.AddStatement(classId, o.Statement!, o.Comment),
        "add_connection" => _structure.AddConnection(classId, o.PortA!, o.PortB!, o.Comment),
        "remove_connection" => _structure.RemoveConnection(classId, o.PortA!, o.PortB!),
        "set_component_placement" => _diagram.SetComponentPlacement(classId, o.Name!, o.X1!.Value, o.Y1!.Value,
            o.X2!.Value, o.Y2!.Value, o.Rotation ?? 0),
        "set_class_description" => _documentation.SetClassDescription(classId, o.Description!),
        "set_component_description" => _documentation.SetComponentDescription(classId, o.Name!, o.Description!),
        "set_class_documentation" => _documentation.SetClassDocumentation(classId, o.Info, o.Revisions),
        "suppress_rule" => _suppression.SuppressRule(classId, o.RuleId!, o.Component, o.Reason),
        "accept_spelling" => _suppression.AcceptSpellingInClass(classId, o.Word!, o.Reason),
        "correct_spelling" => _spelling.CorrectSpelling(classId, o.Word!, o.NewWord!),
        _ => Task.FromResult<object>(new ToolError($"Unknown operation '{o.Op}'.")),
    };

    /// <summary>
    /// Replace the one occurrence of <paramref name="oldText"/> in the class's source, or with
    /// <paramref name="all"/> every occurrence: a name used throughout a class is otherwise renamed with a
    /// shell <c>sed</c>, as an agent did in the Dymola scenario (B503). Either way text that is not there
    /// is refused, and the result is parse-checked before it is written. Line endings are
    /// matched whichever the caller used, since an agent's text has the line feeds it was given and the
    /// file may have carriage returns too. Nothing else is loose: indentation and spacing must match, so a
    /// near miss is an error naming what to do rather than a guess about which text was meant.
    /// </summary>
    private async Task<object> Replace(string classId, string oldText, string newText, bool all)
    {
        var (ctx, error) = ClassBodyEditor.Open(_libraries, classId);
        if (error is not null)
            return error;

        var code = ctx!.ClassCode;
        var eol = code.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var find = Normalise(oldText, eol);
        var with = Normalise(newText, eol);
        if (find == with)
            return new ToolError("old and new are the same, so there is nothing to change.");

        var count = Occurrences(code, find);
        if (count == 0)
            return new ToolError(
                $"The text to replace is not in '{classId}'. It must match the class's source exactly, indentation " +
                "and spacing included. Copy it from mlqt_get_class_source with includeAnnotations=true: by default " +
                "that hides annotations, so its text is not all of the class.");
        if (count > 1 && !all)
            return new ToolError(
                $"The text to replace occurs {count} times in '{classId}'. Include more of the text around it, so " +
                "that it occurs once, or pass all=true to replace every occurrence.");

        string newCode;
        if (all)
            newCode = code.Replace(find, with, StringComparison.Ordinal);
        else
        {
            var at = code.IndexOf(find, StringComparison.Ordinal);
            newCode = code[..at] + with + code[(at + find.Length)..];
        }

        // A changed name would leave the graph holding a class that is no longer in the file, and miss
        // every reference to it; that is what mlqt_rename_class is for.
        var oldName = ModelicaName.LeafOf(classId);
        var newName = ModelicaParserHelper.ExtractModels(newCode).FirstOrDefault()?.Name;
        if (newName is not null && newName != oldName)
            return new ToolError(
                $"The replacement would rename the class from '{oldName}' to '{newName}'. Use mlqt_rename_class, " +
                "which also changes every reference to it.");

        var applied = await ClassBodyEditor.ApplyAsync(_libraries, _resources, _session, ctx, newCode, preview: false, "edit the class");
        // How many, as a note on the answer: replacing more than the caller meant is the risk of all=true.
        return all && applied is ClassEditResult result
            ? new StructureEditResult(classId, result.FilePath, result.PreviewOnly, Changed: true, result.AffectedCount,
                result.NewFileContent, $"Replaced {count} occurrence{(count == 1 ? "" : "s")}.")
            : applied;
    }

    private static string Normalise(string text, string eol) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", eol, StringComparison.Ordinal);

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }
}
