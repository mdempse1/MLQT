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
/// <c>mlqt_write_class</c> and <c>mlqt_format</c>: whole classes and whole libraries in one call. In the
/// 2026-10-18 sessions <c>create_class</c> was called 27-64 times a session and <c>format_class</c> 97 times
/// in one, once per file (B503). Both call the existing tools for each class or file rather than repeat
/// them.
/// </summary>
[Obfuscation]
[McpServerToolType]
public sealed class WriteFormatTools
{
    private readonly ILibraryDataService _libraries;
    private readonly EditTools _edit;
    private readonly FormattingTools _formatting;

    public WriteFormatTools(ILibraryDataService libraries, IRepositoryService repositories,
        IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _edit = new EditTools(libraries, resources, session);
        _formatting = new FormattingTools(libraries, repositories, resources, session);
    }

    [McpServerTool(Name = "mlqt_write_class", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Write one or more whole Modelica classes, each {parentId, source, standalone?}: a class not yet " +
                 "in its parent is created there, placed on disk as mlqt_create_class does. One already there is " +
                 "replaced only with replace=true, so a mistaken name cannot overwrite a class. Classes are written " +
                 "in order, so a later one can go in a package an earlier one created; if one fails, the ones " +
                 "before it stay written and none after it is tried. To change part of a class, use " +
                 "mlqt_edit_class instead. preview=true returns each file without writing (each class is then " +
                 "previewed on its own).")]
    public async Task<object> WriteClass(
        [Description("The classes, in order. Each: {parentId: the package to put it in, source: the whole class, standalone?: own file or not}.")]
        IReadOnlyList<ClassToWrite> classes,
        [Description("Replace a class that already exists. Default false.")] bool replace = false,
        [Description("Return the files without writing. Default false.")] bool preview = false)
    {
        if (classes is null || classes.Count == 0)
            return new ToolError("classes must hold at least one {parentId, source}.");

        var written = new List<WrittenClassItem>();
        for (var i = 0; i < classes.Count; i++)
        {
            var item = classes[i];
            var name = string.IsNullOrWhiteSpace(item.Source) ? null : ModelicaParserHelper.ExtractModels(item.Source).FirstOrDefault()?.Name;
            if (name is null)
                return Failed(i, "its source holds no class that parses.");
            var id = string.IsNullOrWhiteSpace(item.ParentId) ? name : $"{item.ParentId}.{name}";

            object result;
            string action;
            if (_libraries.GetModelById(id) is not null)
            {
                if (!replace)
                    return Failed(i, $"'{id}' already exists. Pass replace=true to replace it, or use mlqt_edit_class to change part of it.");
                result = await _edit.UpdateClassSource(id, item.Source, preview);
                action = "replaced";
            }
            else
            {
                result = await _edit.CreateClass(item.ParentId, item.Source, item.Standalone, preview);
                action = "created";
            }

            switch (result)
            {
                case ToolError error:
                    return Failed(i, error.Error);
                case CreateClassResult created:
                    written.Add(new WrittenClassItem(created.NewClassId, action, created.FilePath, created.NewFileContent));
                    break;
                case UpdateClassSourceResult updated:
                    written.Add(new WrittenClassItem(updated.ClassId, action, updated.FilePath ?? string.Empty, updated.NewFileContent));
                    break;
            }
        }
        return new WriteClassResult(preview, written);

        ToolError Failed(int index, string why) => new(
            $"Class {index} was not written: {why}" +
            (written.Count == 0 ? " Nothing was written." :
                $" Written before it: {string.Join(", ", written.Select(w => w.ClassId))}; nothing after it was tried."));
    }

    [McpServerTool(Name = "mlqt_format", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Format Modelica code with MLQT's formatter. Give exactly one of: classId (the file holding " +
                 "that class), library (every file of a loaded library, by name or id) or source (text, returned " +
                 "formatted; nothing is written). The ordering options are those of mlqt_format_class. A file " +
                 "with syntax errors, or with a class excluded from formatting, is left as it is and reported. " +
                 "preview=true formats without writing.")]
    public async Task<object> Format(
        [Description("A class: its whole file is formatted.")] string? classId = null,
        [Description("A loaded library, by name or id: every file of it is formatted.")] string? library = null,
        [Description("Modelica text to format and return. Nothing is written.")] string? source = null,
        [Description("Emit at most one of each section; default false.")] bool oneOfEachSection = false,
        [Description("Move import statements to the top; default false.")] bool importStatementsFirst = false,
        [Description("Order component declarations before nested class definitions; default false.")] bool componentsBeforeClasses = false,
        [Description("Order declarations as mlqt_format_class's declarationOrder does; default false.")] bool declarationOrder = false,
        [Description("Format without writing; default false.")] bool preview = false)
    {
        var given = new[] { classId, library, source }.Count(x => !string.IsNullOrWhiteSpace(x));
        if (given != 1)
            return new ToolError("Give exactly one of classId, library or source.");

        if (!string.IsNullOrWhiteSpace(source))
            return _formatting.FormatCode(source, oneOfEachSection, importStatementsFirst, componentsBeforeClasses, declarationOrder);
        if (!string.IsNullOrWhiteSpace(classId))
            return await _formatting.FormatClass(classId, oneOfEachSection, importStatementsFirst, componentsBeforeClasses, declarationOrder, preview);

        var (lib, error) = EntityResolver.ResolveLibrary(_libraries, library!);
        if (error is not null)
            return error;
        if (lib!.IsReadOnly)
            return new ToolError($"'{lib.Name}' is a read-only library; nothing in it can be formatted.");

        // One class per file, chosen before anything is written: each format reloads its file.
        var oneClassPerFile = _libraries.GetAllModels()
            .Where(m => !m.IsExternalStub && m.ContainingFileId is not null && _libraries.GetOwningLibrary(m.Id)?.Id == lib.Id)
            .GroupBy(m => m.ContainingFileId!, StringComparer.Ordinal)
            .Select(g => g.OrderBy(m => m.Id.Length).ThenBy(m => m.Id, StringComparer.Ordinal).First().Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var changed = new List<string>();
        var failures = new List<FormatFailureItem>();
        foreach (var id in oneClassPerFile)
        {
            switch (await _formatting.FormatClass(id, oneOfEachSection, importStatementsFirst, componentsBeforeClasses, declarationOrder, preview))
            {
                case FormatClassResult { Changed: true, FilePath: { } path }:
                    changed.Add(path);
                    break;
                case ToolError failure:
                    failures.Add(new FormatFailureItem(id, failure.Error));
                    break;
            }
        }
        return new LibraryFormatResult(lib.Name, preview, oneClassPerFile.Count, changed.Count, changed, failures);
    }
}
