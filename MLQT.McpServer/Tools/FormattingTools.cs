using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Modelica code formatting. format_code is stateless (source in, formatted source out).
/// format_class formats the file containing a loaded class and, unless preview is set, writes the
/// updated file to disk and refreshes the in-memory graph.
/// </summary>
[McpServerToolType]
public sealed class FormattingTools
{
    private readonly ILibraryDataService _libraries;

    public FormattingTools(ILibraryDataService libraries) => _libraries = libraries;

    [McpServerTool(Name = "format_code")]
    [Description("Format an arbitrary Modelica source snippet and return the formatted text " +
                "(stateless — no library needed, nothing written to disk). Options control section " +
                "ordering. Annotations are preserved.")]
    public object FormatCode(
        [Description("Modelica source code to format.")] string source,
        [Description("Emit at most one of each section (public/protected/equation/...); default false.")]
        bool oneOfEachSection = false,
        [Description("Move import statements to the top; default false.")] bool importStatementsFirst = false,
        [Description("Order component declarations before nested class definitions; default false.")]
        bool componentsBeforeClasses = false,
        [Description("Maximum line length before wrapping; default 100.")] int maxLineLength = 100)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new ToolError("source must be non-empty Modelica code.");

        try
        {
            var (parseTree, tokenStream) = ModelicaParserHelper.ParseWithTokens(source);
            var renderer = new ModelicaRenderer(
                renderForCodeEditor: false,
                showAnnotations: true,
                excludeClassDefinitions: false,
                tokenStream: tokenStream,
                classNamesToExclude: null,
                maxLineLength: maxLineLength,
                oneOfEachSection: oneOfEachSection,
                importsFirst: importStatementsFirst,
                componentsBeforeClasses: componentsBeforeClasses);
            renderer.VisitStored_definition(parseTree);
            var formatted = string.Join("\n", renderer.Code);
            return new FormatCodeResult(formatted);
        }
        catch (Exception ex)
        {
            return new ToolError($"Formatting failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "format_class")]
    [Description("Format the .mo file that contains a loaded class using the given ordering options. " +
                "By default the reformatted file is written to disk and the in-memory graph is " +
                "refreshed; set preview=true to return the formatted text without writing. Note this " +
                "reformats the whole containing file (all classes stored in it), matching how MLQT " +
                "saves files.")]
    public async Task<object> FormatClass(
        [Description("Fully-qualified class id whose file should be formatted.")] string classId,
        [Description("Emit at most one of each section; default false.")] bool oneOfEachSection = false,
        [Description("Move import statements to the top; default false.")] bool importStatementsFirst = false,
        [Description("Order component declarations before nested class definitions; default false.")]
        bool componentsBeforeClasses = false,
        [Description("Return the formatted text without writing to disk or updating the graph; default false.")]
        bool preview = false)
    {
        var node = _libraries.GetModelById(classId);
        if (node is null)
            return NotFound(classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be formatted.");

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        string rendered;
        try
        {
            rendered = ModelicaPackageSaver.RenderFileOwnerModel(
                ctx.FileOwner, oneOfEachSection, importStatementsFirst, componentsBeforeClasses);
        }
        catch (Exception ex)
        {
            return new ToolError($"Formatting failed: {ex.Message}");
        }

        if (preview)
            return new FormatClassResult(classId, PreviewOnly: true, Changed: false, ctx.FilePath, rendered);

        var original = File.Exists(ctx.FilePath) ? await File.ReadAllTextAsync(ctx.FilePath) : null;
        var changed = original is null || NormalizeEol(original) != NormalizeEol(rendered);
        if (changed)
        {
            await File.WriteAllTextAsync(ctx.FilePath, rendered);
            await _libraries.ReloadFileAsync(ctx.FilePath);
        }

        return new FormatClassResult(classId, PreviewOnly: false, Changed: changed, ctx.FilePath, rendered);
    }

    private static string NormalizeEol(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static ToolError NotFound(string classId) =>
        new($"No class with id '{classId}'. Ensure a library is loaded and the id is fully-qualified; " +
            "use search_classes to find it.");
}
