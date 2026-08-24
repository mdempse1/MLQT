using ModelicaGraph.DataTypes;
using ModelicaParser.DataTypes;
using ModelicaParser.Helpers;
using ModelicaParser.Visitors;
using MLQT.McpServer.Dtos;
using MLQT.McpServer.Services;
using MLQT.Services.Interfaces;

namespace MLQT.McpServer.Helpers;

/// <summary>The resolved context for a surgical class-body edit.</summary>
internal sealed record ClassEditContext(
    ModelNode Node,
    ModelNode FileOwner,
    string FilePath,
    string ClassCode,
    string OwnerCode,
    ClassBodyLayout Layout);

/// <summary>The outcome of persisting a class-body edit (or a preview).</summary>
internal sealed record ClassEditResult(string FilePath, bool PreviewOnly, int AffectedCount, string? NewFileContent);

/// <summary>
/// Shared machinery for section-aware surgical edits: resolves a class, its file owner and its body
/// layout, then persists a transformed class body by splicing it back into the file, parse-checking,
/// pre-flighting writability, writing, reloading and refreshing dependencies — the same path
/// update_class_source uses, so single-element edits are as safe as whole-class replacement.
/// </summary>
internal static class ClassBodyEditor
{
    /// <summary>Resolve everything an edit needs. Returns (ctx, null) or (null, ToolError).</summary>
    public static (ClassEditContext? ctx, object? error) Open(ILibraryDataService libraries, string classId)
    {
        var node = libraries.GetModelById(classId);
        if (node is null)
            return (null, ToolDiagnostics.ClassNotFound(libraries, classId));
        if (node.IsParseFailurePlaceholder)
            return (null, new ToolError($"Class '{classId}' failed to parse and cannot be edited."));

        var owner = ModelFilePersistence.ResolveFileOwner(libraries, classId);
        if (owner is null)
            return (null, new ToolError($"Could not locate the source file for '{classId}'."));

        var classCode = node.Definition.ModelicaCode ?? string.Empty;
        var layout = ClassBodyLocator.Analyze(classCode);
        if (!layout.Found)
            return (null, new ToolError($"Could not analyse the body of '{classId}' (it may be a short class definition)."));

        return (new ClassEditContext(node, owner.FileOwner, owner.FilePath, classCode,
            owner.FileOwner.Definition.ModelicaCode ?? string.Empty, layout), null);
    }

    /// <summary>Persist a transformed class body. Returns a <see cref="ClassEditResult"/> or a ToolError.</summary>
    public static async Task<object> ApplyAsync(
        ILibraryDataService libraries, IExternalResourceService resources, SessionState session,
        ClassEditContext ctx, string newClassCode, bool preview, string operation)
    {
        string newOwnerCode;
        if (ctx.Node.Id == ctx.FileOwner.Id)
        {
            newOwnerCode = newClassCode;
        }
        else
        {
            if (CountOccurrences(ctx.OwnerCode, ctx.ClassCode) != 1)
                return new ToolError("Could not uniquely locate the class within its file (cached source may be stale). Reload the library and retry.");
            newOwnerCode = ReplaceFirst(ctx.OwnerCode, ctx.ClassCode, newClassCode);
        }

        return await PersistOwnerAsync(libraries, resources, session, ctx.FileOwner, ctx.FilePath, newOwnerCode, preview, operation);
    }

    /// <summary>
    /// Persist a transformed *file-owner* body (the whole-file slice): re-add the within clause,
    /// parse-check, preview/writability-gate, write, reload and refresh. Used by edits that produce a
    /// new owner body directly (e.g. suppress_rule, which places an annotation onto a class located by
    /// name path within the owner rather than splicing a class slice).
    /// </summary>
    public static async Task<object> PersistOwnerAsync(
        ILibraryDataService libraries, IExternalResourceService resources, SessionState session,
        ModelNode fileOwner, string filePath, string newOwnerCode, bool preview, string operation)
    {
        var fileContent = PrependWithinClause(newOwnerCode, fileOwner.ParentModelName);

        var (_, errors) = ModelicaParserHelper.ParseWithErrors(fileContent);
        if (errors.Count > 0)
            return new ToolError($"The edit would make '{filePath}' unparseable ({DescribeErrors(errors)}). Nothing was changed.");

        if (preview)
            return new ClassEditResult(filePath, PreviewOnly: true, AffectedCount: 0, fileContent);

        if (FileWritability.RequireWritable(filePath, operation) is { } readOnly)
            return readOnly;

        await ModelicaFileEncoding.WriteAllTextAsync(filePath, fileContent);
        var affected = await libraries.ReloadFileAsync(filePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, libraries, resources, session);
        return new ClassEditResult(filePath, PreviewOnly: false, affected.Count, null);
    }

    /// <summary>
    /// Persist a complete file's new content (already carrying its within clause) directly: parse-check,
    /// preview/writability-gate, write, reload and refresh. Used by edits made against the on-disk file
    /// text rather than an in-memory class slice — the file is the ground truth and always holds the full
    /// nested structure, whereas a package node's stored <c>ModelicaCode</c> can be a formatting "shell"
    /// that omits nested standalone classes.
    /// </summary>
    public static async Task<object> PersistFileContentAsync(
        ILibraryDataService libraries, IExternalResourceService resources, SessionState session,
        string filePath, string newFileContent, bool preview, string operation)
    {
        var (_, errors) = ModelicaParserHelper.ParseWithErrors(newFileContent);
        if (errors.Count > 0)
            return new ToolError($"The edit would make '{filePath}' unparseable ({DescribeErrors(errors)}). Nothing was changed.");

        if (preview)
            return new ClassEditResult(filePath, PreviewOnly: true, AffectedCount: 0, newFileContent);

        if (FileWritability.RequireWritable(filePath, operation) is { } readOnly)
            return readOnly;

        await ModelicaFileEncoding.WriteAllTextAsync(filePath, newFileContent);
        var affected = await libraries.ReloadFileAsync(filePath);
        await GraphRefresh.RefreshAfterEditAsync(affected, libraries, resources, session);
        return new ClassEditResult(filePath, PreviewOnly: false, affected.Count, null);
    }

    private static string PrependWithinClause(string ownerCode, string? parentModelName)
    {
        if (ownerCode.StartsWith("within", StringComparison.Ordinal))
            return ownerCode;
        return string.IsNullOrEmpty(parentModelName)
            ? "within;\n" + ownerCode
            : $"within {parentModelName};\n{ownerCode}";
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(needle))
            return 0;
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += needle.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string source, string oldValue, string newValue)
    {
        var idx = source.IndexOf(oldValue, StringComparison.Ordinal);
        return idx < 0 ? source : source[..idx] + newValue + source[(idx + oldValue.Length)..];
    }

    private static string DescribeErrors(IReadOnlyList<ParserError> errors)
    {
        var shown = errors.Take(5).Select(e => $"line {e.Line}:{e.CharPosition} {e.Message}");
        return string.Join("; ", shown) + (errors.Count > 5 ? $" (+{errors.Count - 5} more)" : "");
    }
}
