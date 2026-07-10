using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaParser.DataTypes;
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
    [Description("Format one or more COMPLETE Modelica class definitions and return the formatted text " +
                "(stateless — no library needed, nothing written to disk). The input must be a whole " +
                "class definition, e.g. 'model X ... end X;' (or a package / record / block / function / " +
                "connector / type / class). It CANNOT format a fragment on its own — a bare equation, a " +
                "single component declaration, or an expression — because MLQT only formats complete " +
                "classes; wrap such a fragment in a class first. Syntax errors in the input are reported " +
                "(not silently formatted into malformed output). To format a class already loaded from " +
                "disk, use format_class. Options control section ordering; annotations are preserved.")]
    public object FormatCode(
        [Description("Modelica source: one or more complete class definitions (e.g. 'model X ... end X;').")]
        string source,
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
            var (parseTree, tokenStream, errors) = ModelicaParserHelper.ParseWithTokensAndErrors(source);

            // Formatting invalid Modelica produces unreliable output (e.g. 'type = Real;' -> 'type ;'),
            // so report syntax errors rather than silently returning garbage.
            if (errors.Count > 0)
                return new ToolError(
                    $"The input has {errors.Count} Modelica syntax error(s) and cannot be reliably " +
                    $"formatted: {DescribeErrors(errors)}. format_code needs a complete, valid class " +
                    "definition (e.g. 'model X ... end X;').");

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

            // The renderer only emits output for complete class definitions. A fragment that parses
            // without error but is not a class (e.g. a comment) yields an empty string — turn that
            // silent no-op into actionable guidance.
            if (string.IsNullOrWhiteSpace(formatted))
                return new ToolError(
                    "Could not format this input. format_code needs a COMPLETE Modelica class definition " +
                    "(e.g. 'model X ... end X;', or a package / record / block / function). A bare equation, " +
                    "component declaration, or expression cannot be formatted on its own — wrap it in a class, " +
                    "or use format_class to format a class already loaded from disk.");

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
                "refreshed; set preview=true to return the formatted text without writing. Reformats the " +
                "whole containing file (all classes stored in it), matching how MLQT saves files. If the " +
                "class or its file has Modelica syntax errors, the syntax errors are reported and nothing " +
                "is formatted or written (fix them first).")]
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
            return ToolDiagnostics.ClassNotFound(_libraries, classId);
        if (node.IsParseFailurePlaceholder)
            return new ToolError($"Class '{classId}' failed to parse and cannot be formatted.");

        var ctx = ModelFilePersistence.ResolveFileOwner(_libraries, classId);
        if (ctx is null)
            return new ToolError($"Could not locate the source file for '{classId}'.");

        // Refuse to reformat (and overwrite) a file that has syntax errors — formatting invalid Modelica
        // produces unreliable output. Parser errors are captured per model at load time.
        var syntaxErrors = _libraries.CombinedGraph
            .GetModelsInFile(node.ContainingFileId!)
            .SelectMany(m => m.Definition.ParserErrors)
            .ToList();
        if (syntaxErrors.Count > 0)
            return new ToolError(
                $"'{classId}' cannot be formatted: its file has {syntaxErrors.Count} Modelica syntax " +
                $"error(s): {DescribeErrors(syntaxErrors)}. Fix the syntax first — format_class will not " +
                "overwrite the file with malformed output.");

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

    private static string DescribeErrors(IReadOnlyList<ParserError> errors)
    {
        var shown = errors.Take(5).Select(e => $"line {e.Line}:{e.CharPosition} {e.Message}");
        var more = errors.Count > 5 ? $" (+{errors.Count - 5} more)" : "";
        return string.Join("; ", shown) + more;
    }

    private static string NormalizeEol(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
}
