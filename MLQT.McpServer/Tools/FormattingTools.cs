using System.Reflection;
using System.ComponentModel;
using ModelContextProtocol.Server;
using ModelicaGraph;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Helpers;
using MLQT.McpServer.Services;
using MLQT.Services.Helpers;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Tools;

/// <summary>
/// Modelica code formatting. mlqt_format is stateless (source in, formatted source out).
/// mlqt_format formats the file containing a loaded class and, unless preview is set, writes the
/// updated file to disk and refreshes the in-memory graph.
/// </summary>
[Obfuscation]
public sealed class FormattingTools
{
    private readonly ILibraryDataService _libraries;
    private readonly IRepositoryService _repositories;
    private readonly IExternalResourceService _resources;
    private readonly SessionState _session;

    public FormattingTools(ILibraryDataService libraries, IRepositoryService repositories,
        IExternalResourceService resources, SessionState session)
    {
        _libraries = libraries;
        _repositories = repositories;
        _resources = resources;
        _session = session;
    }

    internal object FormatCode(
        string source,
        bool oneOfEachSection = false,
        bool importStatementsFirst = false,
        bool componentsBeforeClasses = false,
        bool declarationOrder = false,
        int maxLineLength = 100)
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
                    $"formatted: {DescribeErrors(errors)}. mlqt_format needs a complete, valid class " +
                    "definition (e.g. 'model X ... end X;').");

            var renderer = new ModelicaRenderer(
                renderForCodeEditor: false,
                showAnnotations: true,
                excludeClassDefinitions: false,
                tokenStream: tokenStream,
                classNamesToExclude: null,
                maxLineLength: maxLineLength,
                formatting: new FormattingOptions(
                    OneOfEachSection: oneOfEachSection,
                    ImportsFirst: importStatementsFirst,
                    ComponentsBeforeClasses: componentsBeforeClasses,
                    DeclarationOrder: declarationOrder));
            renderer.VisitStored_definition(parseTree);
            var formatted = string.Join("\n", renderer.Code);

            // The renderer only emits output for complete class definitions. A fragment that parses
            // without error but is not a class (e.g. a comment) yields an empty string — turn that
            // silent no-op into actionable guidance.
            if (string.IsNullOrWhiteSpace(formatted))
                return new ToolError(
                    "Could not format this input. mlqt_format needs a COMPLETE Modelica class definition " +
                    "(e.g. 'model X ... end X;', or a package / record / block / function). A bare equation, " +
                    "component declaration, or expression cannot be formatted on its own — wrap it in a class, " +
                    "or pass classId to format a class already loaded from disk.");

            return new FormatCodeResult(formatted);
        }
        catch (Exception ex)
        {
            return new ToolError($"Formatting failed: {ex.Message}");
        }
    }

    internal async Task<object> FormatClass(
        string classId,
        bool oneOfEachSection = false,
        bool importStatementsFirst = false,
        bool componentsBeforeClasses = false,
        bool declarationOrder = false,
        bool preview = false)
    {
        if (ReadOnlyClassGuard.Refuse(_libraries, classId, "format this class") is { } readOnlyClass)
            return readOnlyClass;

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
                $"error(s): {DescribeErrors(syntaxErrors)}. Fix the syntax first — mlqt_format will not " +
                "overwrite the file with malformed output.");

        if (!File.Exists(ctx.FilePath))
            return new ToolError($"'{classId}' has no file on disk to format.");

        // A file is reformatted whole or not at all, so one class in it that opted out - by
        // __MLQT(format=false) / preserveOrder=true, or by its repository's excluded list - keeps the
        // whole file as it is. The same rule and the same method the GUI's incremental format uses;
        // this tool asked neither, and rewrote exactly the classes that had said not to (B313).
        var settings = RepositorySettings.ForClass(_libraries, _repositories, classId);
        var excluded = _libraries.CombinedGraph.GetModelsInFile(node.ContainingFileId!)
            .FirstOrDefault(m => FormattingExclusion.Excludes(m, settings));
        if (excluded is not null)
            return new ToolError(
                $"'{ctx.FilePath}' was not formatted: '{excluded.Id}' in it is excluded from formatting " +
                "(__MLQT(format=false) or preserveOrder=true in its source, or its repository's " +
                "FormattingExcludedModels list). Nothing was written.");

        var original = await ModelicaFileEncoding.ReadAllTextOnlyAsync(ctx.FilePath);

        // Format the file as it is on disk, not the owner's stored source. Style checking trims a
        // package's inline standalone children out of its ModelicaCode — each child has its own node
        // — so rendering from the stored source would write the file back without those classes,
        // which is the opposite of what this tool promises ("all classes stored in it").
        var owner = ctx.FileOwner;
        var snapshot = owner.TakeSourceSnapshot();
        owner.Definition.ModelicaCode = original;

        string rendered;
        try
        {
            // The same lookup mlqt_check gives MLQT.Style.DeclarationOrder, keyed by the same class,
            // so the order written here is the order the rule asks for.
            rendered = ModelicaPackageSaver.RenderFileOwnerModel(
                owner,
                new FormattingOptions(
                    OneOfEachSection: oneOfEachSection,
                    ImportsFirst: importStatementsFirst,
                    ComponentsBeforeClasses: componentsBeforeClasses,
                    DeclarationOrder: declarationOrder),
                isSimpleType: declarationOrder ? StyleChecking.CreateSimpleTypeLookup(_libraries.CombinedGraph) : null);
        }
        catch (Exception ex)
        {
            owner.RestoreSource(snapshot);
            return new ToolError($"Formatting failed: {ex.Message}");
        }

        if (preview)
        {
            // Nothing is written, so put the node back the way it was.
            owner.RestoreSource(snapshot);
            return new FormatClassResult(classId, PreviewOnly: true, Changed: false, ctx.FilePath, rendered);
        }

        // Compared as it would be written, not as it was rendered: the writer ends the file with a
        // newline, so comparing the raw render against a file that has one reports every already
        // formatted file as changed and rewrites it on every call.
        var changed = NormalizeEol(original) != NormalizeEol(ModelicaFileEncoding.EnsureFinalNewline(rendered));
        if (changed)
        {
            if (FileWritability.RequireWritable(ctx.FilePath, "format this file") is { } readOnly)
            {
                owner.RestoreSource(snapshot);
                return readOnly;
            }

            await ModelicaFileEncoding.WriteAllTextAsync(ctx.FilePath, rendered);
            var affected = await _libraries.ReloadFileAsync(ctx.FilePath);
            await GraphRefresh.RefreshAfterEditAsync(affected, _libraries, _resources, _session);
        }
        else
        {
            // The file was already formatted, so nothing was written and the node keeps its own source.
            owner.RestoreSource(snapshot);
        }

        return new FormatClassResult(classId, PreviewOnly: false, Changed: changed, ctx.FilePath, rendered);
    }

    internal const string DeclarationOrderDescription =
        "Write declarations in the order inputs and outputs, constants, parameters, variables, " +
        "components (MLQT.Style.DeclarationOrder); only takes effect with oneOfEachSection, " +
        "importStatementsFirst and componentsBeforeClasses all on. A record keeps its fields in source order, since that is its constructor's " +
        "signature. Default false.";

    private static string DescribeErrors(IReadOnlyList<ParserError> errors)
    {
        var shown = errors.Take(5).Select(e => $"line {e.Line}:{e.CharPosition} {e.Message}");
        var more = errors.Count > 5 ? $" (+{errors.Count - 5} more)" : "";
        return string.Join("; ", shown) + more;
    }

    private static string NormalizeEol(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');
}
